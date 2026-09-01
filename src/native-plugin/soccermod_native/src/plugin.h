/**
 * SoccerMod Native Physics Bridge
 *
 * A minimal Metamod:Source plugin that exposes ConCommands the CS2 SoccerMod
 * CounterStrikeSharp plugin can call over Server.ExecuteCommand for the one
 * thing C# cannot do: fire an entity input carrying a typed Vector value.
 *
 * Background: CounterStrikeSharp's CEntityInstance.AcceptInput only accepts
 * a string value.  ApplyAbsVelocityImpulse and ApplyLocalAngularVelocityImpulse
 * are registered inputs on every CBaseEntity (confirmed present as strings in
 * libserver.so) but require a FIELD_VECTOR variant_t, which a string value
 * cannot produce.  This plugin resolves CEntityInstance::AcceptInput's real
 * address (which takes a variant_t*, not a string) via a byte-signature scan,
 * and GameEntitySystem() via a byte offset into IGameResourceService — both
 * values sourced from the actively-maintained Source2ZE/CS2Fixes project's
 * gamedata (GPLv3), not independently reverse-engineered.  See
 * docs/ball-foundation/2026-08-29-native-plugin.md for the full rationale,
 * the exact signatures/offsets used, and their provenance.
 */
#ifndef _INCLUDE_SM2NATIVE_PLUGIN_H_
#define _INCLUDE_SM2NATIVE_PLUGIN_H_

#include <ISmmPlugin.h>
#include "version_gen.h"

class MMSPlugin : public ISmmPlugin, public IMetamodListener
{
public:
	bool Load(PluginId id, ISmmAPI *ismm, char *error, size_t maxlen, bool late);
	bool Unload(char *error, size_t maxlen);

public:
	const char *GetAuthor() { return PLUGIN_AUTHOR; }
	const char *GetName() { return PLUGIN_DISPLAY_NAME; }
	const char *GetDescription() { return PLUGIN_DESCRIPTION; }
	const char *GetURL() { return PLUGIN_URL; }
	const char *GetLicense() { return PLUGIN_LICENSE; }
	const char *GetVersion() { return PLUGIN_FULL_VERSION; }
	const char *GetDate() { return __DATE__; }
	const char *GetLogTag() { return PLUGIN_LOGTAG; }
};

extern MMSPlugin g_ThisPlugin;

PLUGIN_GLOBALVARS();

#endif
