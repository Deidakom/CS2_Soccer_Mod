// Host-side checks for the native bridge's signature scanner and gamedata
// reader (header-only, no game SDK needed). Built and run by
// test/test_native_bridge.py; prints one line per failure, exits non-zero.
#include "../../src/native-plugin/soccermod_native/src/gamedata.h"
#include "../../src/native-plugin/soccermod_native/src/sigscan.h"

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>

static int failures = 0;

static void Check(bool condition, const char* what)
{
	if (!condition)
	{
		printf("FAIL: %s\n", what);
		failures++;
	}
}

static std::string WriteTemp(const char* text)
{
	char path[] = "/tmp/sm2native-gamedata-XXXXXX";
	int fd = mkstemp(path);
	FILE* file = fdopen(fd, "wb");
	fputs(text, file);
	fclose(file);
	return path;
}

int main()
{
	using namespace sm2native;

	// Scanner: code with the 1.41.8.2 AcceptInput prologue once, a prologue
	// that appears twice, and the old prologue absent.
	std::vector<unsigned char> code(4096, 0xCC);
	const unsigned char accept[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x49, 0x89, 0xFF, 0x41, 0x56, 0x48, 0x8D, 0xBD,
		0x10, 0x20, 0x30, 0x40, 0x41, 0x55, 0x4C, 0x8D, 0xAD };
	const unsigned char twice[] = { 0x55, 0x48, 0x89, 0xE5, 0x53, 0x48, 0x89, 0xFB };
	std::copy(accept, accept + sizeof(accept), code.begin() + 1000);
	std::copy(twice, twice + sizeof(twice), code.begin() + 200);
	std::copy(twice, twice + sizeof(twice), code.begin() + 3000);
	// Split the buffer into two ranges so a match in the second one is found too.
	std::vector<ExecRange> ranges { { reinterpret_cast<uintptr_t>(code.data()), 2048 },
		{ reinterpret_cast<uintptr_t>(code.data() + 2048), 2048 } };

	int matches = -1;
	void* found = FindUniquePattern(ranges, "55 48 89 E5 41 57 49 89 FF 41 56 48 8D BD ? ? ? ? 41 55 4C 8D AD", &matches);
	Check(found == code.data() + 1000 && matches == 1, "unique signature with wildcards resolves to its offset");
	found = FindUniquePattern(ranges, "55 48 89 e5 41 57 49 89 ff 41 56 48 8d bd ?? ?? ?? ?? 41 55 4c 8d ad", &matches);
	Check(found == code.data() + 1000 && matches == 1, "lower-case bytes and ?? wildcards are accepted");
	found = FindUniquePattern(ranges, "55 48 89 E5 53 48 89 FB", &matches);
	Check(found == nullptr && matches == 2, "a signature matching twice is refused");
	found = FindUniquePattern(ranges, "55 48 89 E5 41 56 49 89 FE 41 55 48 8D 7D", &matches);
	Check(found == nullptr && matches == 0, "the pre-1.41.8.2 signature is not found");
	// 3000 filler bytes exist in the buffer, but no range holds that many.
	std::string filler;
	for (int i = 0; i < 3000; i++)
	{
		filler += i ? " CC" : "CC";
	}
	found = FindUniquePattern(ranges, filler.c_str(), &matches);
	Check(found == nullptr && matches == 0, "a pattern longer than every range never matches");

	const char* malformed[] = { "", "   ", "? ? ?", "5", "5G", "55 4", "55?", "55 48x", "0x55 48" };
	for (const char* pattern : malformed)
	{
		matches = -1;
		found = FindUniquePattern(ranges, pattern, &matches);
		Check(found == nullptr && matches == 0, pattern);
	}

	// Gamedata: CounterStrikeSharp's layout, with a decoy "linux" in an earlier
	// entry and braces/escaped quotes inside strings before the value.
	std::string path = WriteTemp(
		"{\n"
		"  \"CCSPlayerController_ChangeTeam\": { \"offsets\": { \"windows\": 105, \"linux\": 104 } },\n"
		"  \"CBaseEntity_IsPlayerPawn\": { \"offsets\": { \"linux\": 170 } },\n"
		"  \"CEntityInstance_AcceptInput\": {\n"
		"    \"signatures\": {\n"
		"      \"library\": \"server\",\n"
		"      \"note\": \"an escaped \\\" } is no brace\",\n"
		"      \"windows\": \"48 89 5C 24 ? 48 89 6C 24\",\n"
		"      \"linux\"  :  \"55 48 89 E5 41 57 49 89 FF 41 56 48 8D BD ? ? ? ? 41 55 4C 8D AD\"\n"
		"    }\n"
		"  },\n"
		"  \"CEntityIOOutput_FireOutputInternal\": { \"signatures\": { \"linux\": \"55 48 89 E5\" } }\n"
		"}\n");
	std::string signature;
	Check(ReadGamedataSignature(path.c_str(), "CEntityInstance_AcceptInput", signature)
		&& signature == "55 48 89 E5 41 57 49 89 FF 41 56 48 8D BD ? ? ? ? 41 55 4C 8D AD",
		"the AcceptInput linux signature is read from CounterStrikeSharp gamedata");
	Check(ReadGamedataSignature(path.c_str(), "CEntityIOOutput_FireOutputInternal", signature) && signature == "55 48 89 E5",
		"the last entry is read too");
	Check(!ReadGamedataSignature(path.c_str(), "CheckTransmit", signature), "a missing entry is not found");
	Check(ReadGamedataSignature(path.c_str(), "CBaseEntity_IsPlayerPawn", signature) == false,
		"an offset-only entry has no signature");
	remove(path.c_str());

	path = WriteTemp("{ \"CEntityInstance_AcceptInput\": { \"signatures\": { \"windows\": \"48 89\" } }, \"Other\": { \"linux\": \"55\" } }");
	Check(!ReadGamedataSignature(path.c_str(), "CEntityInstance_AcceptInput", signature),
		"a linux value of a later entry is never borrowed");
	remove(path.c_str());

	path = WriteTemp("{ \"CEntityInstance_AcceptInput\": { \"signatures\": { \"linux\": \"55 48");
	Check(!ReadGamedataSignature(path.c_str(), "CEntityInstance_AcceptInput", signature), "a truncated file is refused");
	remove(path.c_str());

	Check(!ReadGamedataSignature("/nonexistent/gamedata.json", "CEntityInstance_AcceptInput", signature),
		"a missing file is refused");

	if (failures == 0)
	{
		printf("native bridge checks passed\n");
	}
	return failures == 0 ? 0 : 1;
}
