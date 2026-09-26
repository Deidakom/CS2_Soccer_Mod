// Reads one signature for this platform ("linux" or "windows") from
// CounterStrikeSharp's gamedata.json.
//
// CounterStrikeSharp ships the updated signatures with the release that
// supports a new CS2 build, so taking them from its gamedata lets this bridge
// follow CS2 updates without a rebuild.  Deliberately not a JSON parser: the
// value is only ever used if it parses as a byte pattern AND matches exactly
// one place in libserver.so (see FindUniquePattern).
#pragma once

#include <cstdio>
#include <string>

namespace sm2native
{

inline bool ReadGamedataSignature(const char* path, const char* name, std::string& signature)
{
	FILE* file = fopen(path, "rb");
	if (!file)
	{
		return false;
	}
	std::string text;
	char buffer[16384];
	size_t read;
	while ((read = fread(buffer, 1, sizeof(buffer), file)) > 0 && text.size() < (8u << 20))
	{
		text.append(buffer, read);
	}
	fclose(file);

	const std::string key = std::string("\"") + name + "\"";
	size_t open = text.find(key);
	open = open == std::string::npos ? open : text.find('{', open + key.size());
	if (open == std::string::npos)
	{
		return false;
	}
	// The entry ends at the brace matching its opening one (strings skipped).
	size_t end = std::string::npos;
	bool inString = false;
	for (size_t i = open, depth = 0; i < text.size(); i++)
	{
		const char c = text[i];
		if (inString)
		{
			if (c == '\\')
			{
				i++;
			}
			else if (c == '"')
			{
				inString = false;
			}
		}
		else if (c == '"')
		{
			inString = true;
		}
		else if (c == '{')
		{
			depth++;
		}
		else if (c == '}' && --depth == 0)
		{
			end = i;
			break;
		}
	}

#ifdef _WIN32
	const std::string platformKey = "\"windows\"";
#else
	const std::string platformKey = "\"linux\"";
#endif
	size_t at = text.find(platformKey, open);
	if (end == std::string::npos || at == std::string::npos || at > end)
	{
		return false;
	}
	at = text.find_first_not_of(" \t\r\n", at + platformKey.size());
	if (at == std::string::npos || text[at] != ':')
	{
		return false;
	}
	at = text.find_first_not_of(" \t\r\n", at + 1);
	if (at == std::string::npos || text[at] != '"')
	{
		return false;
	}
	const size_t close = text.find('"', at + 1);
	if (close == std::string::npos || close > end)
	{
		return false;
	}
	signature = text.substr(at + 1, close - at - 1);
	return true;
}

} // namespace sm2native
