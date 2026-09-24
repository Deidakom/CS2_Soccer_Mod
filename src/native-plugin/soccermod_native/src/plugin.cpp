/**
 * SoccerMod Native Physics Bridge -- see plugin.h for the full rationale.
 *
 * The "CEntityInstance_AcceptInput" linux byte signature -- the real
 * (non-virtual) AcceptInput implementation, which takes a variant_t* rather
 * than a string -- is NOT independently derived.  It is read at load time from
 * CounterStrikeSharp's gamedata.json (roflmuffin/CounterStrikeSharp, GPLv3),
 * which ships the updated signature with the release that supports a new CS2
 * build, so the bridge follows CS2 updates without a rebuild.  The built-in
 * fallbacks are the same values from CounterStrikeSharp v1.0.375 (CS2
 * 1.41.8.2) and v1.0.374, the latter identical to Source2ZE/CS2Fixes.  A
 * signature is only used when it matches exactly one place in libserver.so.
 *
 * Entity resolution deliberately does NOT go through
 * CGameEntitySystem::GetEntityIdentity(): that symbol is not dynamically
 * exported by libserver.so (confirmed: absent from `nm -D`), so a native
 * plugin cannot link against it directly, and CS2Fixes itself only reaches
 * it through its own large, separately-vendored gamedata/signature
 * infrastructure.  Instead, the CounterStrikeSharp side already holds a
 * valid CEntityInstance* for the ball (`_ball.Handle`, an IntPtr -- the same
 * native pointer its own working AcceptInput(string) calls already use) and
 * passes that pointer's numeric value straight through.  This needs no
 * offset, no entity-system lookup, and no additional gamedata dependency.
 *
 * Build: build-linux.sh next to this folder's AMBuildScript.
 */
#include "plugin.h"
#include "gamedata.h"
#include "sigscan.h"

#include <entity2/entityinstance.h>
#include <tier1/convar.h>
#include <variant.h>
#include <vector.h>

#include <cinttypes>
#include <cstdio>
#include <cstdlib>
#include <string>

MMSPlugin g_ThisPlugin;
IVEngineServer *engine = NULL;
ICvar *icvar = NULL;

// CEntityInstance::AcceptInput in libserver.so, with CounterStrikeSharp's
// prototype (the trailing output id and parameter map are passed as 0/null,
// exactly as CounterStrikeSharp does).  Resolved once at Load(); every
// ConCommand below checks this for null before using it, so a bad resolve
// degrades to "command unavailable" instead of a null-pointer call.
using AcceptInputFn = void (*)(CEntityInstance *pThis, const char *pInputName,
	CEntityInstance *pActivator, CEntityInstance *pCaller, variant_t *value,
	int nOutputID, void *pParamMap);
static AcceptInputFn g_fnAcceptInput = nullptr;

// Fallbacks when CounterStrikeSharp's gamedata is missing or does not match:
// CS2 1.41.8.2 (2026-09-22) and later, then the builds before it.
static const char *const kBuiltinAcceptInputSignatures[] = {
	"55 48 89 E5 41 57 49 89 FF 41 56 48 8D BD ? ? ? ? 41 55 4C 8D AD",
	"55 48 89 E5 41 56 49 89 FE 41 55 48 8D 7D",
};

static void ResolveAcceptInput(const char *baseDir)
{
	char modulePath[512] = { 0 };
	auto ranges = sm2native::FindExecutableRanges("libserver.so", modulePath, sizeof(modulePath));
	if (ranges.empty())
	{
		META_CONPRINTF("[SM2NATIVE] could not locate libserver.so executable segments; "
			"native impulse commands will be unavailable.\n");
		return;
	}
	META_CONPRINTF("[SM2NATIVE] scanning module: %s (%zu exec range(s)) \n",
		modulePath[0] ? modulePath : "<none>", ranges.size());

	struct Candidate
	{
		const char *source;
		std::string signature;
	};
	std::vector<Candidate> candidates;
	char gamedataPath[768];
	snprintf(gamedataPath, sizeof(gamedataPath), "%s/addons/counterstrikesharp/gamedata/gamedata.json", baseDir);
	std::string gamedataSignature;
	if (sm2native::ReadGamedataSignature(gamedataPath, "CEntityInstance_AcceptInput", gamedataSignature))
	{
		candidates.push_back({ "CounterStrikeSharp gamedata", gamedataSignature });
	}
	for (const char *signature : kBuiltinAcceptInputSignatures)
	{
		candidates.push_back({ "built-in", signature });
	}

	for (const auto &candidate : candidates)
	{
		int matches = 0;
		void *found = sm2native::FindUniquePattern(ranges, candidate.signature.c_str(), &matches);
		if (found)
		{
			g_fnAcceptInput = reinterpret_cast<AcceptInputFn>(found);
			META_CONPRINTF("[SM2NATIVE] resolved CEntityInstance_AcceptInput at %p (%s signature)\n",
				found, candidate.source);
			return;
		}
		META_CONPRINTF("[SM2NATIVE] %s signature for CEntityInstance_AcceptInput %s: %s\n", candidate.source,
			matches == 0 ? "not found" : "is ambiguous", candidate.signature.c_str());
	}
	META_CONPRINTF("[SM2NATIVE] signature for CEntityInstance_AcceptInput not found; "
		"native impulse commands will be unavailable.\n");
}

static CEntityInstance *ParsePointerArg(const char *arg)
{
	uintptr_t value = 0;
	if (sscanf(arg, "%" SCNxPTR, &value) != 1 || value == 0)
	{
		return nullptr;
	}
	return reinterpret_cast<CEntityInstance *>(value);
}

// Read-only: reinterprets the given pointer as a CEntityInstance* and prints
// its classname, purely to confirm the value C# passed (from
// css_sm2ball_native_handle) really points at a live entity before any
// write path (the impulse commands below) is trusted.  A garbage pointer
// here would either print gibberish/crash -- run this BEFORE ever calling
// sm2_native_impulse with a new pointer source.
CON_COMMAND_F(sm2_native_selftest, "SoccerMod native bridge: read-only entity pointer check", FCVAR_GAMEDLL)
{
	if (args.ArgC() < 2)
	{
		META_CONPRINTF("[SM2NATIVE] usage: sm2_native_selftest <hexPointer>\n");
		return;
	}

	auto *entity = ParsePointerArg(args.Arg(1));
	if (!entity)
	{
		META_CONPRINTF("[SM2NATIVE] selftest: could not parse pointer arg '%s'\n", args.Arg(1));
		return;
	}

	META_CONPRINTF("[SM2NATIVE] selftest OK: pointer=%p classname=\"%s\"\n",
		(void *)entity, entity->GetClassname());
}

static void FireVectorInput(const CCommand &args, const char *inputName)
{
	if (!g_fnAcceptInput)
	{
		META_CONPRINTF("[SM2NATIVE] %s FAILED: CEntityInstance_AcceptInput was not resolved at load.\n", inputName);
		return;
	}

	if (args.ArgC() < 5)
	{
		META_CONPRINTF("[SM2NATIVE] usage: <command> <hexPointer> <x> <y> <z>\n");
		return;
	}

	auto *entity = ParsePointerArg(args.Arg(1));
	if (!entity)
	{
		META_CONPRINTF("[SM2NATIVE] %s FAILED: could not parse pointer arg '%s'.\n", inputName, args.Arg(1));
		return;
	}

	Vector value(
		static_cast<float>(atof(args.Arg(2))),
		static_cast<float>(atof(args.Arg(3))),
		static_cast<float>(atof(args.Arg(4))));

	variant_t variant(value);
	g_fnAcceptInput(entity, inputName, nullptr, nullptr, &variant, 0, nullptr);
	META_CONPRINTF("[SM2NATIVE] %s sent to %p: (%.2f, %.2f, %.2f)\n",
		inputName, (void *)entity, value.x, value.y, value.z);
}

CON_COMMAND_F(sm2_native_impulse, "SoccerMod native bridge: typed ApplyAbsVelocityImpulse", FCVAR_GAMEDLL)
{
	FireVectorInput(args, "ApplyAbsVelocityImpulse");
}

CON_COMMAND_F(sm2_native_angular_impulse, "SoccerMod native bridge: typed ApplyLocalAngularVelocityImpulse", FCVAR_GAMEDLL)
{
	FireVectorInput(args, "ApplyLocalAngularVelocityImpulse");
}

PLUGIN_EXPOSE(MMSPlugin, g_ThisPlugin);
bool MMSPlugin::Load(PluginId id, ISmmAPI *ismm, char *error, size_t maxlen, bool late)
{
	PLUGIN_SAVEVARS();

	GET_V_IFACE_CURRENT(GetEngineFactory, engine, IVEngineServer, INTERFACEVERSION_VENGINESERVER);
	GET_V_IFACE_CURRENT(GetEngineFactory, icvar, ICvar, CVAR_INTERFACE_VERSION);

	g_pCVar = icvar;
	META_CONVAR_REGISTER(FCVAR_RELEASE | FCVAR_GAMEDLL);

	ResolveAcceptInput(ismm->GetBaseDir());

	META_CONPRINTF("[SM2NATIVE] loaded.\n");

	return true;
}

bool MMSPlugin::Unload(char *error, size_t maxlen)
{
	return true;
}
