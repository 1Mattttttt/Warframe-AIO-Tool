#pragma once
#include <string>
#include <utility>

namespace xorstr_impl {
    template <size_t N, size_t K>
    struct XorStringA {
        char data[N];

        constexpr XorStringA(const char(&str)[N]) : data{} {
            for (size_t i = 0; i < N; ++i) {
                data[i] = str[i] ^ static_cast<char>((K + i * 7) & 0xFF);
            }
        }

        std::string decrypt() const {
            std::string result;
            result.resize(N - 1);
            for (size_t i = 0; i < N - 1; ++i) {
                result[i] = data[i] ^ static_cast<char>((K + i * 7) & 0xFF);
            }
            return result;
        }

        operator std::string() const {
            return decrypt();
        }
    };

    template <size_t N, size_t K>
    struct XorStringW {
        wchar_t data[N];

        constexpr XorStringW(const wchar_t(&str)[N]) : data{} {
            for (size_t i = 0; i < N; ++i) {
                data[i] = str[i] ^ static_cast<wchar_t>((K + i * 13) & 0xFFFF);
            }
        }

        std::wstring decrypt() const {
            std::wstring result;
            result.resize(N - 1);
            for (size_t i = 0; i < N - 1; ++i) {
                result[i] = data[i] ^ static_cast<wchar_t>((K + i * 13) & 0xFFFF);
            }
            return result;
        }

        operator std::wstring() const {
            return decrypt();
        }
    };
}

#define _xor_a(str) (xorstr_impl::XorStringA<sizeof(str), (__LINE__ * 37 + 101) % 256>(str).decrypt())
#define _xor_w(str) (xorstr_impl::XorStringW<sizeof(str) / sizeof(wchar_t), (__LINE__ * 43 + 211) % 65536>(str).decrypt())
