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
// this only needs one pattern, in one module, found once at load time, so a
// ~40-line dl_iterate_phdr scan is easier to audit than pulling in an
// external module abstraction we would not otherwise need.
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

// pattern like "55 48 89 E5 41 56 49 89 FE 41 55 48 8D 7D", '?' = wildcard byte.
inline void* FindPattern(const std::vector<ExecRange>& ranges, const char* pattern)
{
	std::vector<int> bytes; // -1 = wildcard
	for (const char* p = pattern; *p;)
	{
		while (*p == ' ')
		{
			p++;
		}
		if (!*p)
		{
			break;
		}
		if (*p == '?')
		{
			bytes.push_back(-1);
			while (*p && *p != ' ')
			{
				p++;
			}
			continue;
		}
		bytes.push_back(static_cast<int>(strtoul(p, nullptr, 16)));
		while (*p && *p != ' ')
		{
			p++;
		}
	}

	for (const auto& range : ranges)
	{
		auto* mem = reinterpret_cast<const unsigned char*>(range.base);
		if (range.size < bytes.size())
		{
			continue;
		}
		for (size_t i = 0; i + bytes.size() <= range.size; i++)
		{
			bool match = true;
			for (size_t j = 0; j < bytes.size(); j++)
			{
				if (bytes[j] != -1 && mem[i + j] != static_cast<unsigned char>(bytes[j]))
				{
					match = false;
					break;
				}
			}
			if (match)
			{
				return const_cast<unsigned char*>(mem + i);
			}
		}
	}

	return nullptr;
}

} // namespace sm2native
