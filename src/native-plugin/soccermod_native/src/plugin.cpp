/**
 * SoccerMod Native Physics Bridge -- see plugin.h for the full rationale.
 *
 * One gamedata value is sourced from Source2ZE/CS2Fixes
 * (https://github.com/Source2ZE/CS2Fixes, GPLv3), NOT independently derived:
 * the "CEntityInstance_AcceptInput" linux byte signature -- the real
 * (non-virtual) AcceptInput implementation, which takes a variant_t* rather
 * than a string.
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
 * See docs/ball-foundation/2026-08-29-native-plugin.md for the full
 * rationale and provenance.
 */
#include "plugin.h"
#include "sigscan.h"

#include <entity2/entityinstance.h>
#include <tier1/convar.h>
#include <variant.h>
#include <vector.h>

#include <cinttypes>
#include <cstdio>
#include <cstdlib>

MMSPlugin g_ThisPlugin;
IVEngineServer *engine = NULL;
ICvar *icvar = NULL;

// CS2Fixes gamedata (linux): "CEntityInstance_AcceptInput" signature, in
// libserver.so.  Resolved once at Load(); every ConCommand below checks
// this for null before using it, so a bad resolve degrades to "command
// unavailable" instead of a null-pointer call.
using AcceptInputFn = void (*)(CEntityInstance *pThis, const char *pInputName,
	CEntityInstance *pActivator, CEntityInstance *pCaller, variant_t *value);
static AcceptInputFn g_fnAcceptInput = nullptr;

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
	g_fnAcceptInput(entity, inputName, nullptr, nullptr, &variant);
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

	char matchedModulePath[512] = { 0 };
	auto ranges = sm2native::FindExecutableRanges("libserver.so", matchedModulePath, sizeof(matchedModulePath));
	if (ranges.empty())
	{
		META_CONPRINTF("[SM2NATIVE] could not locate libserver.so executable segments; "
			"native impulse commands will be unavailable.\n");
	}
	else
	{
		META_CONPRINTF("[SM2NATIVE] scanning module: %s (%zu exec range(s)) \n",
			matchedModulePath[0] ? matchedModulePath : "<none>", ranges.size());
		void *found = sm2native::FindPattern(
			ranges, "55 48 89 E5 41 56 49 89 FE 41 55 48 8D 7D");
		if (found)
		{
			g_fnAcceptInput = reinterpret_cast<AcceptInputFn>(found);
			META_CONPRINTF("[SM2NATIVE] resolved CEntityInstance_AcceptInput at %p\n", found);
		}
		else
		{
			META_CONPRINTF("[SM2NATIVE] signature for CEntityInstance_AcceptInput not found; "
				"native impulse commands will be unavailable.\n");
		}
	}

	META_CONPRINTF("[SM2NATIVE] loaded.\n");

	return true;
}

bool MMSPlugin::Unload(char *error, size_t maxlen)
{
	return true;
}
