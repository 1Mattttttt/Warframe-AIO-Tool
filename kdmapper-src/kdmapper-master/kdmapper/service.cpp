#include "service.hpp"
#include <Windows.h>
#include <string>
#include <iostream>

#include "utils.hpp"
#include "nt.hpp"

NTSTATUS service::RegisterAndStart(const std::wstring& driver_path, const std::wstring& serviceName) {
	const static DWORD ServiceTypeKernel = 1;
	const std::wstring servicesPath = _xor_w(L"SYSTEM\\CurrentControlSet\\Services\\") + serviceName;
	const std::wstring nPath = _xor_w(L"\\??\\") + driver_path;

	HKEY dservice = NULL;
	LSTATUS status = RegCreateKeyW(HKEY_LOCAL_MACHINE, servicesPath.c_str(), &dservice);
	if (status != ERROR_SUCCESS) {
		kdmLog(L"[-] Can't create service key" << std::endl);
		return STATUS_REGISTRY_IO_FAILED;
	}

	status = RegSetKeyValueW(dservice, NULL, _xor_w(L"ImagePath").c_str(), REG_EXPAND_SZ, nPath.c_str(), (DWORD)(nPath.size() * sizeof(wchar_t)));
	if (status != ERROR_SUCCESS) {
		RegCloseKey(dservice);
		RegDeleteTreeW(HKEY_LOCAL_MACHINE, servicesPath.c_str());
		kdmLog(L"[-] Can't create ImagePath registry value" << std::endl);
		return STATUS_REGISTRY_IO_FAILED;
	}

	status = RegSetKeyValueW(dservice, NULL, _xor_w(L"Type").c_str(), REG_DWORD, &ServiceTypeKernel, sizeof(DWORD));
	if (status != ERROR_SUCCESS) {
		RegCloseKey(dservice);
		RegDeleteTreeW(HKEY_LOCAL_MACHINE, servicesPath.c_str());
		kdmLog(L"[-] Can't create Type registry value" << std::endl);
		return STATUS_REGISTRY_IO_FAILED;
	}

	RegCloseKey(dservice);

	HMODULE ntdll = GetModuleHandleA(_xor_a("ntdll.dll").c_str());
	if (ntdll == NULL) {
		RegDeleteTreeW(HKEY_LOCAL_MACHINE, servicesPath.c_str());
		return STATUS_UNSUCCESSFUL;
	}

	ULONG SE_LOAD_DRIVER_PRIVILEGE = 10UL;
	BOOLEAN SeLoadDriverWasEnabled;
	NTSTATUS ntStatus = nt::RtlAdjustPrivilege(SE_LOAD_DRIVER_PRIVILEGE, TRUE, FALSE, &SeLoadDriverWasEnabled);
	if (!NT_SUCCESS(ntStatus)) {
		RegDeleteTreeW(HKEY_LOCAL_MACHINE, servicesPath.c_str());
		kdmLog(L"Fatal error: failed to acquire SE_LOAD_DRIVER_PRIVILEGE. Run as Administrator." << std::endl);
		return ntStatus;
	}

	std::wstring wdriver_reg_path = _xor_w(L"\\Registry\\Machine\\System\\CurrentControlSet\\Services\\") + serviceName;
	UNICODE_STRING serviceStr;
	RtlInitUnicodeString(&serviceStr, wdriver_reg_path.c_str());

	ntStatus = nt::NtLoadDriver(&serviceStr);

	kdmLog(L"[+] NtLoadDriver Status 0x" << std::hex << ntStatus << std::endl);

	if (ntStatus == STATUS_IMAGE_CERT_REVOKED) {
		kdmLog(L"[-] Vulnerable driver list enabled, blocked driver loading." << std::endl);
		kdmLog(L"[-] Set VulnerableDriverBlocklistEnable to 0 in HKLM\\SYSTEM\\CurrentControlSet\\Control\\CI\\Config" << std::endl);
	}
	else if (ntStatus == STATUS_ACCESS_DENIED || ntStatus == STATUS_INSUFFICIENT_RESOURCES) {
		kdmLog(L"[-] Access Denied (0x" << std::hex << ntStatus << L"), security software may be blocking load" << std::endl);
	}

	if (!NT_SUCCESS(ntStatus)) {
		RegDeleteTreeW(HKEY_LOCAL_MACHINE, servicesPath.c_str());
	}
	else {
		kdmUtils::ClearSecurityEventLogs();
	}

	return ntStatus;
}

NTSTATUS service::StopAndRemove(const std::wstring& serviceName) {
	HMODULE ntdll = GetModuleHandleA(_xor_a("ntdll.dll").c_str());
	if (ntdll == NULL)
		return STATUS_UNSUCCESSFUL;

	std::wstring wdriver_reg_path = _xor_w(L"\\Registry\\Machine\\System\\CurrentControlSet\\Services\\") + serviceName;
	UNICODE_STRING serviceStr;
	RtlInitUnicodeString(&serviceStr, wdriver_reg_path.c_str());

	HKEY driver_service = NULL;
	std::wstring servicesPath = _xor_w(L"SYSTEM\\CurrentControlSet\\Services\\") + serviceName;
	LSTATUS status = RegOpenKeyW(HKEY_LOCAL_MACHINE, servicesPath.c_str(), &driver_service);
	if (status != ERROR_SUCCESS) {
		if (status == ERROR_FILE_NOT_FOUND) {
			return STATUS_SUCCESS;
		}
		return STATUS_REGISTRY_IO_FAILED;
	}
	RegCloseKey(driver_service);

	NTSTATUS st = nt::NtUnloadDriver(&serviceStr);
	kdmLog(L"[+] NtUnloadDriver Status 0x" << std::hex << st << std::endl);

	RegDeleteTreeW(HKEY_LOCAL_MACHINE, servicesPath.c_str());
	kdmUtils::ClearSecurityEventLogs();

	return st;
}
