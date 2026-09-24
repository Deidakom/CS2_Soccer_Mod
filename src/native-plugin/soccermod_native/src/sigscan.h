// Minimal, self-contained byte-signature scanner for a loaded ELF module.
//
// The only thing we need this for is CEntityInstance::AcceptInput (the
// non-virtual member function that resolves an input name to its handler
// and calls it with a typed variant_t).  CounterStrikeSharp's AcceptInput
// only accepts a string value; the real function accepts a variant_t that
// can carry a typed Vector, which is what lets us fire
// ApplyAbsVelocityImpulse / ApplyLocalAngularVelocityImpulse correctly.
//
// Deliberately NOT vendoring a larger existing signature-scanning framework:
// this only needs one function, in one module, found once at load time, so a
// small dl_iterate_phdr scan is easier to audit than pulling in an external
// module abstraction we would not otherwise need.
#pragma once

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <link.h>
#include <vector>

namespace sm2native
{

struct ExecRange
{
	uintptr_t base;
	size_t size;
};

// Finds the base address and executable (PF_X) segment ranges of a loaded
// shared object by name (e.g. "libserver.so"), via dl_iterate_phdr — the
// standard POSIX way to enumerate the modules already mapped into this
// process, which is exactly what we are: a shared object dlopen'd by
// Metamod into the running CS2 server process.
struct ModuleFindContext
{
	const char* wantedName;
	std::vector<ExecRange> ranges;
	char matchedPath[512];
};

inline int ModuleFindCallback(struct dl_phdr_info* info, size_t, void* data)
{
	auto* ctx = reinterpret_cast<ModuleFindContext*>(data);
	if (!info->dlpi_name || !info->dlpi_name[0])
	{
		return 0;
	}

	const char* slash = strrchr(info->dlpi_name, '/');
	const char* base = slash ? slash + 1 : info->dlpi_name;
	if (strcmp(base, ctx->wantedName) != 0)
	{
		return 0;
	}

	// CRITICAL: Metamod:Source injects its own proxy module ALSO named
	// "libserver.so" under addons/metamod/bin/. Its tiny code segment does
	// NOT contain the game functions, so matching it (and stopping) made the
	// AcceptInput scan silently fail. Skip any metamod path; only the real
	// game module below csgo/bin/ carries the .text we need.
	if (strstr(info->dlpi_name, "/metamod/") != nullptr)
	{
		return 0;
	}

	snprintf(ctx->matchedPath, sizeof(ctx->matchedPath), "%s", info->dlpi_name);

	for (int i = 0; i < info->dlpi_phnum; i++)
	{
		const auto& phdr = info->dlpi_phdr[i];
		if (phdr.p_type == PT_LOAD && (phdr.p_flags & PF_X))
		{
			ctx->ranges.push_back(
				{ info->dlpi_addr + phdr.p_vaddr, static_cast<size_t>(phdr.p_memsz) });
		}
	}

	return 1;
}

inline std::vector<ExecRange> FindExecutableRanges(const char* moduleName, char* outMatchedPath = nullptr, size_t outLen = 0)
{
	ModuleFindContext ctx { moduleName, {}, { 0 } };
	dl_iterate_phdr(ModuleFindCallback, &ctx);
	if (outMatchedPath && outLen > 0)
	{
		snprintf(outMatchedPath, outLen, "%s", ctx.matchedPath);
	}
	return ctx.ranges;
}

inline int HexDigit(char c)
{
	if (c >= '0' && c <= '9')
	{
		return c - '0';
	}
	if (c >= 'A' && c <= 'F')
	{
		return c - 'A' + 10;
	}
	if (c >= 'a' && c <= 'f')
	{
		return c - 'a' + 10;
	}
	return -1;
}

// Parses a pattern like "55 48 89 E5 41 57 ? ? 4C" into bytes (-1 = wildcard).
// Anything else is rejected, so a damaged pattern (e.g. from a gamedata file)
// is never scanned.
inline bool ParsePattern(const char* pattern, std::vector<int>& bytes)
{
	bytes.clear();
	bool anyFixed = false;
	for (const char* p = pattern; *p;)
	{
		if (*p == ' ')
		{
			p++;
			continue;
		}
		if (*p == '?')
		{
			bytes.push_back(-1);
			p += p[1] == '?' ? 2 : 1;
		}
		else
		{
			int high = HexDigit(p[0]);
			int low = high < 0 ? -1 : HexDigit(p[1]);
			if (low < 0)
			{
				return false;
			}
			bytes.push_back(high << 4 | low);
			anyFixed = true;
			p += 2;
		}
		if (*p && *p != ' ')
		{
			return false;
		}
	}
	return anyFixed;
}

// Returns the only match of the pattern in the ranges. A malformed, absent or
// ambiguous pattern returns nullptr: after a game update an old pattern can
// match a different function, and calling that would crash the server.
// matches receives 0, 1 or 2 (= more than one).
inline void* FindUniquePattern(const std::vector<ExecRange>& ranges, const char* pattern, int* matches = nullptr)
{
	std::vector<int> bytes;
	void* first = nullptr;
	int count = 0;
	if (ParsePattern(pattern, bytes))
	{
		for (const auto& range : ranges)
		{
			auto* mem = reinterpret_cast<const unsigned char*>(range.base);
			for (size_t i = 0; count < 2 && i + bytes.size() <= range.size; i++)
			{
				size_t j = 0;
				while (j < bytes.size() && (bytes[j] < 0 || mem[i + j] == bytes[j]))
				{
					j++;
				}
				if (j == bytes.size() && count++ == 0)
				{
					first = const_cast<unsigned char*>(mem + i);
				}
			}
		}
	}
	if (matches)
	{
		*matches = count;
	}
	return count == 1 ? first : nullptr;
}

} // namespace sm2native
