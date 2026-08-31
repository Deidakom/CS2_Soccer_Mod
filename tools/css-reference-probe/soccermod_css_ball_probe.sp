#include <sourcemod>
#include <sdktools>
#include <sdkhooks>
#include <cstrike>

#pragma semicolon 1
#pragma newdecls required

public Plugin myinfo =
{
    name = "SoccerMod CS:S Ball Reference Probe",
    author = "Sergi + Codex",
    description = "Passive hit capture plus isolated XSL physics reference trials",
    version = "1.3.0",
    url = ""
};

static const char BALL_CLASS[] = "func_physbox";
static const char BALL_TARGET[] = "ballon";
static const char TRIAL_BALL_TARGET[] = "sm2_xsl_reference_probe";
static const float TRIAL_SAMPLE_INTERVAL = 0.02;

int g_BallRef = INVALID_ENT_REFERENCE;
int g_HitSequence = 0;
int g_TrialBallRef = INVALID_ENT_REFERENCE;
int g_TrialSequence = 0;
int g_TrialSampleIndex = 0;
int g_TrialSampleLimit = 0;
char g_TrialKind[16];
float g_TrialPreviousPosition[3];
float g_TrialPreviousTime = 0.0;
Handle g_TrialTimer = INVALID_HANDLE;

int g_ImpactBallRef = INVALID_ENT_REFERENCE;
int g_ImpactClientUserId = 0;
int g_ImpactSequence = 0;
int g_ImpactSampleIndex = 0;
float g_ImpactLaunchSpeed = 0.0;
float g_ImpactMaxPlanarSpeed = 0.0;
bool g_ImpactHurtEvent = false;
Handle g_ImpactTimer = INVALID_HANDLE;

public void OnPluginStart()
{
    HookEvent("round_start", Event_RoundStart, EventHookMode_PostNoCopy);
    HookEvent("player_hurt", Event_ImpactPlayerHurt, EventHookMode_Post);
    HookEntityOutput(BALL_CLASS, "OnDamaged", OnBallDamagedOutput);
    RegAdminCmd("sm_xslref_trial", Command_XslReferenceTrial, ADMFLAG_ROOT,
        "Run an isolated exact-XSL roll, wall, or drop trial.");
    RegAdminCmd("sm_xslref_stop", Command_StopXslReferenceTrial, ADMFLAG_ROOT,
        "Stop and remove the isolated XSL reference trial ball.");
    RegAdminCmd("sm_xslref_impact", Command_XslReferenceImpact, ADMFLAG_ROOT,
        "Measure native CS:S player knockback from the exact XSL ball at a requested speed.");
    FindAndHookBall("plugin_start");
}

public void OnMapStart()
{
    g_BallRef = INVALID_ENT_REFERENCE;
    g_HitSequence = 0;
    g_TrialSequence = 0;
    ResetTrialState(false);
    ResetImpactState(false);
}

public void OnMapEnd()
{
    ResetTrialState(false);
    ResetImpactState(false);
}

public void OnPluginEnd()
{
    ResetTrialState(true);
    ResetImpactState(true);
}

public void Event_ImpactPlayerHurt(Event event, const char[] name, bool dontBroadcast)
{
    if (g_ImpactClientUserId == 0 || event.GetInt("userid") != g_ImpactClientUserId)
    {
        return;
    }

    g_ImpactHurtEvent = true;
    char weapon[64];
    event.GetString("weapon", weapon, sizeof(weapon));
    LogMessage(
        "[SM2CSSREF] impact_hurt seq=%d dmgHealth=%d dmgArmor=%d health=%d weapon=%s",
        g_ImpactSequence,
        event.GetInt("dmg_health"),
        event.GetInt("dmg_armor"),
        event.GetInt("health"),
        weapon);
}

public void OnEntityCreated(int entity, const char[] classname)
{
    if (!StrEqual(classname, BALL_CLASS))
    {
        return;
    }

    SDKHook(entity, SDKHook_SpawnPost, OnBallSpawnPost);
}

public void OnBallSpawnPost(int entity)
{
    FindAndHookBall("spawn_post");
}

public void Event_RoundStart(Event event, const char[] name, bool dontBroadcast)
{
    CreateTimer(0.1, Timer_FindBall, _, TIMER_FLAG_NO_MAPCHANGE);
}

public Action Timer_FindBall(Handle timer)
{
    FindAndHookBall("round_start");
    return Plugin_Stop;
}

void FindAndHookBall(const char[] reason)
{
    int current = EntRefToEntIndex(g_BallRef);
    if (current > MaxClients && IsValidEntity(current))
    {
        return;
    }

    int entity = -1;
    char target[128];
    while ((entity = FindEntityByClassname(entity, BALL_CLASS)) != -1)
    {
        target[0] = '\0';
        if (HasEntProp(entity, Prop_Data, "m_iName"))
        {
            GetEntPropString(entity, Prop_Data, "m_iName", target, sizeof(target));
        }

        if (!StrEqual(target, BALL_TARGET))
        {
            continue;
        }

        g_BallRef = EntIndexToEntRef(entity);
        SDKHook(entity, SDKHook_OnTakeDamage, OnBallTakeDamage);
        LogMessage("[SM2CSSREF] ball_bound reason=%s entity=%d target=%s", reason, entity, target);
        return;
    }

    LogMessage("[SM2CSSREF] ball_not_found reason=%s", reason);
}

public Action OnBallTakeDamage(
    int victim,
    int &attacker,
    int &inflictor,
    float &damage,
    int &damageType,
    int &weapon,
    float damageForce[3],
    float damagePosition[3])
{
    if (victim != EntRefToEntIndex(g_BallRef)
        || attacker < 1
        || attacker > MaxClients
        || !IsClientInGame(attacker))
    {
        return Plugin_Continue;
    }

    int activeWeapon = GetEntPropEnt(attacker, Prop_Send, "m_hActiveWeapon");
    char weaponClass[64] = "<none>";
    if (activeWeapon > MaxClients && IsValidEntity(activeWeapon))
    {
        GetEntityClassname(activeWeapon, weaponClass, sizeof(weaponClass));
    }

    if (StrContains(weaponClass, "knife", false) == -1)
    {
        return Plugin_Continue;
    }

    float eyePosition[3];
    float eyeAngles[3];
    float ballPosition[3];
    float ballVelocity[3];
    float ballAngularVelocity[3];
    GetClientEyePosition(attacker, eyePosition);
    GetClientEyeAngles(attacker, eyeAngles);
    GetEntPropVector(victim, Prop_Data, "m_vecAbsOrigin", ballPosition);
    GetEntPropVector(victim, Prop_Data, "m_vecVelocity", ballVelocity);
    if (HasEntProp(victim, Prop_Data, "m_vecAngVelocity"))
    {
        GetEntPropVector(victim, Prop_Data, "m_vecAngVelocity", ballAngularVelocity);
    }

    int sequence = ++g_HitSequence;
    LogMessage(
        "[SM2CSSREF] hit seq=%d attacker=%N damage=%.6f damageType=%d weapon=%s eye=(%.6f %.6f %.6f) angles=(%.6f %.6f %.6f) position=(%.6f %.6f %.6f) velocityBefore=(%.6f %.6f %.6f) angularBefore=(%.6f %.6f %.6f) damageForce=(%.6f %.6f %.6f) damagePosition=(%.6f %.6f %.6f)",
        sequence,
        attacker,
        damage,
        damageType,
        weaponClass,
        eyePosition[0], eyePosition[1], eyePosition[2],
        eyeAngles[0], eyeAngles[1], eyeAngles[2],
        ballPosition[0], ballPosition[1], ballPosition[2],
        ballVelocity[0], ballVelocity[1], ballVelocity[2],
        ballAngularVelocity[0], ballAngularVelocity[1], ballAngularVelocity[2],
        damageForce[0], damageForce[1], damageForce[2],
        damagePosition[0], damagePosition[1], damagePosition[2]);

    ScheduleSample(sequence, g_BallRef, 0.0, "next_frame");
    ScheduleSample(sequence, g_BallRef, 0.05, "plus_0.05s");
    ScheduleSample(sequence, g_BallRef, 0.25, "plus_0.25s");
    ScheduleSample(sequence, g_BallRef, 1.0, "plus_1.00s");
    return Plugin_Continue;
}

public void OnBallDamagedOutput(const char[] output, int caller, int activator, float delay)
{
    if (caller <= MaxClients
        || !IsValidEntity(caller)
        || activator < 1
        || activator > MaxClients
        || !IsClientInGame(activator))
    {
        return;
    }

    char target[128];
    if (HasEntProp(caller, Prop_Data, "m_iName"))
    {
        GetEntPropString(caller, Prop_Data, "m_iName", target, sizeof(target));
    }
    if (!StrEqual(target, BALL_TARGET))
    {
        return;
    }

    int activeWeapon = GetEntPropEnt(activator, Prop_Send, "m_hActiveWeapon");
    char weaponClass[64] = "<none>";
    if (activeWeapon > MaxClients && IsValidEntity(activeWeapon))
    {
        GetEntityClassname(activeWeapon, weaponClass, sizeof(weaponClass));
    }
    if (StrContains(weaponClass, "knife", false) == -1)
    {
        return;
    }

    float eyePosition[3];
    float eyeAngles[3];
    float ballPosition[3];
    float ballVelocity[3];
    float ballAngularVelocity[3];
    GetClientEyePosition(activator, eyePosition);
    GetClientEyeAngles(activator, eyeAngles);
    GetEntPropVector(caller, Prop_Data, "m_vecAbsOrigin", ballPosition);
    GetEntPropVector(caller, Prop_Data, "m_vecVelocity", ballVelocity);
    if (HasEntProp(caller, Prop_Data, "m_vecAngVelocity"))
    {
        GetEntPropVector(caller, Prop_Data, "m_vecAngVelocity", ballAngularVelocity);
    }

    int sequence = ++g_HitSequence;
    LogMessage(
        "[SM2CSSREF] output_hit seq=%d attacker=%N weapon=%s eye=(%.6f %.6f %.6f) angles=(%.6f %.6f %.6f) position=(%.6f %.6f %.6f) velocityImmediate=(%.6f %.6f %.6f) speedImmediate=%.6f angularImmediate=(%.6f %.6f %.6f)",
        sequence,
        activator,
        weaponClass,
        eyePosition[0], eyePosition[1], eyePosition[2],
        eyeAngles[0], eyeAngles[1], eyeAngles[2],
        ballPosition[0], ballPosition[1], ballPosition[2],
        ballVelocity[0], ballVelocity[1], ballVelocity[2],
        GetVectorLength(ballVelocity),
        ballAngularVelocity[0], ballAngularVelocity[1], ballAngularVelocity[2]);

    ScheduleSample(sequence, EntIndexToEntRef(caller), 0.0, "next_frame");
    ScheduleSample(sequence, EntIndexToEntRef(caller), 0.05, "plus_0.05s");
    ScheduleSample(sequence, EntIndexToEntRef(caller), 0.25, "plus_0.25s");
    ScheduleSample(sequence, EntIndexToEntRef(caller), 1.0, "plus_1.00s");
}

void ScheduleSample(int sequence, int ballRef, float delay, const char[] stage)
{
    DataPack pack = new DataPack();
    pack.WriteCell(sequence);
    pack.WriteCell(ballRef);
    pack.WriteString(stage);
    CreateTimer(delay, Timer_SampleBall, pack, TIMER_FLAG_NO_MAPCHANGE | TIMER_DATA_HNDL_CLOSE);
}

public Action Timer_SampleBall(Handle timer, DataPack pack)
{
    pack.Reset();
    int sequence = pack.ReadCell();
    int ball = EntRefToEntIndex(pack.ReadCell());
    char stage[32];
    pack.ReadString(stage, sizeof(stage));

    if (ball <= MaxClients || !IsValidEntity(ball))
    {
        LogMessage("[SM2CSSREF] sample seq=%d stage=%s ball_invalid", sequence, stage);
        return Plugin_Stop;
    }

    float position[3];
    float velocity[3];
    float angularVelocity[3];
    GetEntPropVector(ball, Prop_Data, "m_vecAbsOrigin", position);
    GetEntPropVector(ball, Prop_Data, "m_vecVelocity", velocity);
    if (HasEntProp(ball, Prop_Data, "m_vecAngVelocity"))
    {
        GetEntPropVector(ball, Prop_Data, "m_vecAngVelocity", angularVelocity);
    }

    LogMessage(
        "[SM2CSSREF] sample seq=%d stage=%s time=%.6f position=(%.6f %.6f %.6f) velocity=(%.6f %.6f %.6f) speed=%.6f angular=(%.6f %.6f %.6f)",
        sequence,
        stage,
        GetEngineTime(),
        position[0], position[1], position[2],
        velocity[0], velocity[1], velocity[2],
        GetVectorLength(velocity),
        angularVelocity[0], angularVelocity[1], angularVelocity[2]);
    return Plugin_Stop;
}

public Action Command_XslReferenceTrial(int client, int args)
{
    if (args < 1)
    {
        ReplyToCommand(client, "[SM2CSSREF] usage: sm_xslref_trial roll|wall|drop|flight [speed] [angleDegrees]");
        return Plugin_Handled;
    }

    char kind[16];
    GetCmdArg(1, kind, sizeof(kind));
    if (!StrEqual(kind, "roll", false)
        && !StrEqual(kind, "wall", false)
        && !StrEqual(kind, "drop", false)
        && !StrEqual(kind, "flight", false))
    {
        ReplyToCommand(client, "[SM2CSSREF] rejected trial; use roll, wall, drop, or flight");
        return Plugin_Handled;
    }

    // flight: launch at a fixed speed/angle (default 1359.2 u/s @ 10.6 deg -
    // the CS:S measured clean-kick reference) and log the full raw arc every
    // TRIAL_SAMPLE_INTERVAL until it settles, so apex/range/hang-time can be
    // extracted from the log afterward exactly like the roll/wall/drop
    // trials already are - no new derived-stat logic needed in-plugin.
    float flightSpeed = 1359.2;
    float flightAngleDegrees = 10.6;
    if (StrEqual(kind, "flight", false))
    {
        if (args >= 2)
        {
            char speedArg[32];
            GetCmdArg(2, speedArg, sizeof(speedArg));
            flightSpeed = StringToFloat(speedArg);
        }
        if (args >= 3)
        {
            char angleArg[32];
            GetCmdArg(3, angleArg, sizeof(angleArg));
            flightAngleDegrees = StringToFloat(angleArg);
        }
    }

    ResetTrialState(true);
    FindAndHookBall("controlled_trial");
    int referenceBall = EntRefToEntIndex(g_BallRef);
    if (referenceBall <= MaxClients || !IsValidEntity(referenceBall))
    {
        ReplyToCommand(client, "[SM2CSSREF] exact map ball unavailable; trial aborted");
        return Plugin_Handled;
    }

    char inlineModel[32];
    GetEntPropString(referenceBall, Prop_Data, "m_ModelName", inlineModel, sizeof(inlineModel));
    if (inlineModel[0] != '*')
    {
        ReplyToCommand(client, "[SM2CSSREF] reference ball is not an inline XSL hull; trial aborted");
        return Plugin_Handled;
    }

    int trialBall = CreateEntityByName(BALL_CLASS);
    if (trialBall == -1)
    {
        ReplyToCommand(client, "[SM2CSSREF] could not allocate isolated trial ball");
        return Plugin_Handled;
    }

    DispatchKeyValue(trialBall, "model", inlineModel);
    DispatchKeyValue(trialBall, "targetname", TRIAL_BALL_TARGET);
    DispatchKeyValue(trialBall, "spawnflags", "5120");
    DispatchKeyValue(trialBall, "notsolid", "0");
    DispatchKeyValue(trialBall, "preferredcarryangles", "0 0 0");
    DispatchKeyValue(trialBall, "forcetoenablemotion", "0");
    DispatchKeyValue(trialBall, "damagetoenablemotion", "0");
    DispatchKeyValue(trialBall, "massScale", "0");
    DispatchKeyValue(trialBall, "Damagetype", "0");
    DispatchKeyValue(trialBall, "material", "5");
    DispatchKeyValue(trialBall, "health", "0");
    DispatchKeyValue(trialBall, "propdata", "17");
    DispatchKeyValue(trialBall, "PerformanceMode", "0");
    DispatchKeyValue(trialBall, "nodamageforces", "0");

    float origin[3] = { 0.0, 0.0, 17.0 };
    if (StrEqual(kind, "wall", false))
    {
        origin[0] = -900.0;
    }

    char originText[96];
    FormatEx(originText, sizeof(originText), "%.6f %.6f %.6f", origin[0], origin[1], origin[2]);
    DispatchKeyValue(trialBall, "origin", originText);
    if (!DispatchSpawn(trialBall))
    {
        RemoveEntity(trialBall);
        ReplyToCommand(client, "[SM2CSSREF] isolated trial ball failed to spawn");
        return Plugin_Handled;
    }

    ActivateEntity(trialBall);
    TeleportEntity(trialBall, origin, NULL_VECTOR, NULL_VECTOR);
    AcceptEntityInput(trialBall, "Wake");
    g_TrialBallRef = EntIndexToEntRef(trialBall);
    strcopy(g_TrialKind, sizeof(g_TrialKind), kind);
    int sequence = ++g_TrialSequence;
    LogMessage(
        "[SM2CSSREF] trial_prepare seq=%d kind=%s entity=%d model=%s origin=(%.6f %.6f %.6f)",
        sequence, kind, trialBall, inlineModel, origin[0], origin[1], origin[2]);

    if (StrEqual(kind, "drop", false))
    {
        origin[2] = 256.0;
        TeleportEntity(trialBall, origin, NULL_VECTOR, NULL_VECTOR);
        StartTrialSampling(sequence, 200);
    }
    else if (StrEqual(kind, "flight", false))
    {
        // Launch immediately, no 1s settle wait (roll/wall start the ball at
        // rest first; flight needs it moving from frame one). 400 samples at
        // 0.02s = 8s of coverage, generous for any kick-realistic arc.
        float launchAngleRadians = flightAngleDegrees * 3.14159265 / 180.0;
        float launchVelocity[3];
        launchVelocity[0] = Cosine(launchAngleRadians) * flightSpeed;
        launchVelocity[1] = 0.0;
        launchVelocity[2] = Sine(launchAngleRadians) * flightSpeed;
        TeleportEntity(trialBall, origin, NULL_VECTOR, launchVelocity);
        AcceptEntityInput(trialBall, "Wake");
        LogMessage(
            "[SM2CSSREF] trial_flight_launch seq=%d speed=%.3f angleDegrees=%.3f velocity=(%.6f %.6f %.6f)",
            sequence, flightSpeed, flightAngleDegrees,
            launchVelocity[0], launchVelocity[1], launchVelocity[2]);
        StartTrialSampling(sequence, 400);
    }
    else
    {
        DataPack pack = new DataPack();
        pack.WriteCell(sequence);
        CreateTimer(1.0, Timer_StartSettledTrial, pack,
            TIMER_FLAG_NO_MAPCHANGE | TIMER_DATA_HNDL_CLOSE);
    }

    ReplyToCommand(client, "[SM2CSSREF] isolated %s trial %d started", kind, sequence);
    return Plugin_Handled;
}

public Action Command_StopXslReferenceTrial(int client, int args)
{
    ResetTrialState(true);
    ResetImpactState(true);
    ReplyToCommand(client, "[SM2CSSREF] isolated reference trial stopped");
    return Plugin_Handled;
}

public Action Command_XslReferenceImpact(int client, int args)
{
    if (args < 1)
    {
        ReplyToCommand(client, "[SM2CSSREF] usage: sm_xslref_impact <speed 100..3000>");
        return Plugin_Handled;
    }

    char speedArg[32];
    GetCmdArg(1, speedArg, sizeof(speedArg));
    float requestedSpeed = StringToFloat(speedArg);
    if (requestedSpeed < 100.0 || requestedSpeed > 3000.0)
    {
        ReplyToCommand(client, "[SM2CSSREF] impact speed must be 100..3000 u/s");
        return Plugin_Handled;
    }

    ResetTrialState(true);
    ResetImpactState(true);

    int probeClient = CreateFakeClient("SM2 Impact Probe");
    if (probeClient < 1)
    {
        ReplyToCommand(client, "[SM2CSSREF] could not create impact probe client");
        return Plugin_Handled;
    }

    g_ImpactClientUserId = GetClientUserId(probeClient);
    g_ImpactLaunchSpeed = requestedSpeed;
    g_ImpactSequence++;

    CreateTimer(0.10, Timer_PrepareImpactClient, g_ImpactSequence, TIMER_FLAG_NO_MAPCHANGE);
    ReplyToCommand(client, "[SM2CSSREF] native player-impact trial %d preparing at %.1f u/s",
        g_ImpactSequence, g_ImpactLaunchSpeed);
    return Plugin_Handled;
}

public Action Timer_PrepareImpactClient(Handle timer, any sequence)
{
    int probeClient = GetClientOfUserId(g_ImpactClientUserId);
    if (sequence != g_ImpactSequence || probeClient < 1 || !IsClientInGame(probeClient))
    {
        LogMessage("[SM2CSSREF] impact_abort seq=%d reason=probe_client_not_ingame", sequence);
        ResetImpactState(true);
        return Plugin_Stop;
    }

    ChangeClientTeam(probeClient, CS_TEAM_CT);
    CS_RespawnPlayer(probeClient);
    // A lone fake client starts a fresh CS:S round asynchronously; allow the
    // round-start path to finish before checking IsPlayerAlive/launching.
    CreateTimer(5.0, Timer_StartImpactTrial, sequence, TIMER_FLAG_NO_MAPCHANGE);
    return Plugin_Stop;
}

public Action Timer_StartImpactTrial(Handle timer, any sequence)
{
    int probeClient = GetClientOfUserId(g_ImpactClientUserId);
    if (sequence != g_ImpactSequence
        || probeClient < 1
        || !IsClientInGame(probeClient)
        || !IsPlayerAlive(probeClient))
    {
        LogMessage("[SM2CSSREF] impact_abort seq=%d reason=probe_client_invalid", sequence);
        ResetImpactState(true);
        return Plugin_Stop;
    }

    FindAndHookBall("controlled_impact");
    int referenceBall = EntRefToEntIndex(g_BallRef);
    if (referenceBall <= MaxClients || !IsValidEntity(referenceBall))
    {
        LogMessage("[SM2CSSREF] impact_abort seq=%d reason=reference_ball_invalid", sequence);
        ResetImpactState(true);
        return Plugin_Stop;
    }

    char inlineModel[32];
    GetEntPropString(referenceBall, Prop_Data, "m_ModelName", inlineModel, sizeof(inlineModel));
    int impactBall = CreateEntityByName(BALL_CLASS);
    if (inlineModel[0] != '*' || impactBall == -1)
    {
        LogMessage("[SM2CSSREF] impact_abort seq=%d reason=impact_ball_create_failed", sequence);
        ResetImpactState(true);
        return Plugin_Stop;
    }

    DispatchKeyValue(impactBall, "model", inlineModel);
    DispatchKeyValue(impactBall, "targetname", "sm2_xsl_impact_probe");
    DispatchKeyValue(impactBall, "spawnflags", "5120");
    DispatchKeyValue(impactBall, "notsolid", "0");
    DispatchKeyValue(impactBall, "preferredcarryangles", "0 0 0");
    DispatchKeyValue(impactBall, "forcetoenablemotion", "0");
    DispatchKeyValue(impactBall, "damagetoenablemotion", "0");
    DispatchKeyValue(impactBall, "massScale", "0");
    DispatchKeyValue(impactBall, "Damagetype", "0");
    DispatchKeyValue(impactBall, "material", "5");
    DispatchKeyValue(impactBall, "health", "0");
    DispatchKeyValue(impactBall, "propdata", "17");
    DispatchKeyValue(impactBall, "PerformanceMode", "0");
    DispatchKeyValue(impactBall, "nodamageforces", "0");

    // Keep this away from the map-authored ball at (0,0,17); placing the
    // probe client at field centre would start it intersecting that ball and
    // contaminate the trial with an immediate vertical launch.
    float playerOrigin[3] = { 500.0, 0.0, 0.0 };
    float zero[3] = { 0.0, 0.0, 0.0 };
    TeleportEntity(probeClient, playerOrigin, NULL_VECTOR, zero);

    // Start close enough that gravity cannot turn low-speed trials into a
    // foot hit while high-speed trials remain a torso hit.
    float ballOrigin[3] = { 450.0, 0.0, 36.0 };
    char originText[96];
    FormatEx(originText, sizeof(originText), "%.6f %.6f %.6f",
        ballOrigin[0], ballOrigin[1], ballOrigin[2]);
    DispatchKeyValue(impactBall, "origin", originText);
    if (!DispatchSpawn(impactBall))
    {
        RemoveEntity(impactBall);
        LogMessage("[SM2CSSREF] impact_abort seq=%d reason=impact_ball_spawn_failed", sequence);
        ResetImpactState(true);
        return Plugin_Stop;
    }

    ActivateEntity(impactBall);
    SDKHook(impactBall, SDKHook_StartTouchPost, OnImpactBallStartTouchPost);
    float launch[3];
    launch[0] = g_ImpactLaunchSpeed;
    TeleportEntity(impactBall, ballOrigin, NULL_VECTOR, launch);
    AcceptEntityInput(impactBall, "Wake");
    g_ImpactBallRef = EntIndexToEntRef(impactBall);
    g_ImpactSampleIndex = 0;
    g_ImpactMaxPlanarSpeed = 0.0;
    g_ImpactHurtEvent = false;

    int takeDamage = HasEntProp(probeClient, Prop_Data, "m_takedamage")
        ? GetEntProp(probeClient, Prop_Data, "m_takedamage", 1)
        : -1;
    LogMessage(
        "[SM2CSSREF] impact_start seq=%d requestedSpeed=%.1f player=%d health=%d armor=%d takeDamage=%d ballOrigin=(%.1f %.1f %.1f)",
        sequence,
        g_ImpactLaunchSpeed,
        probeClient,
        GetClientHealth(probeClient),
        GetClientArmor(probeClient),
        takeDamage,
        ballOrigin[0], ballOrigin[1], ballOrigin[2]);

    g_ImpactTimer = CreateTimer(TRIAL_SAMPLE_INTERVAL, Timer_SampleImpact, sequence,
        TIMER_REPEAT | TIMER_FLAG_NO_MAPCHANGE);
    return Plugin_Stop;
}

public void OnImpactBallStartTouchPost(int entity, int other)
{
    int impactBall = EntRefToEntIndex(g_ImpactBallRef);
    int probeClient = GetClientOfUserId(g_ImpactClientUserId);
    if (entity != impactBall || other != probeClient || probeClient < 1)
    {
        return;
    }

    float playerVelocity[3];
    float ballOrigin[3];
    GetEntPropVector(probeClient, Prop_Data, "m_vecVelocity", playerVelocity);
    GetEntPropVector(impactBall, Prop_Data, "m_vecAbsOrigin", ballOrigin);
    float playerPlanarSpeed = SquareRoot(
        playerVelocity[0] * playerVelocity[0] + playerVelocity[1] * playerVelocity[1]);
    if (playerPlanarSpeed > g_ImpactMaxPlanarSpeed)
    {
        g_ImpactMaxPlanarSpeed = playerPlanarSpeed;
    }

    LogMessage(
        "[SM2CSSREF] impact_contact seq=%d requestedSpeed=%.1f playerVelocity=(%.3f %.3f %.3f) playerPlanarSpeed=%.3f ballOrigin=(%.3f %.3f %.3f) health=%d",
        g_ImpactSequence,
        g_ImpactLaunchSpeed,
        playerVelocity[0], playerVelocity[1], playerVelocity[2],
        playerPlanarSpeed,
        ballOrigin[0], ballOrigin[1], ballOrigin[2],
        GetClientHealth(probeClient));
}

public Action Timer_SampleImpact(Handle timer, any sequence)
{
    int probeClient = GetClientOfUserId(g_ImpactClientUserId);
    int impactBall = EntRefToEntIndex(g_ImpactBallRef);
    if (timer != g_ImpactTimer
        || sequence != g_ImpactSequence
        || probeClient < 1
        || !IsClientInGame(probeClient)
        || impactBall <= MaxClients
        || !IsValidEntity(impactBall))
    {
        if (timer == g_ImpactTimer) g_ImpactTimer = INVALID_HANDLE;
        LogMessage("[SM2CSSREF] impact_abort seq=%d reason=sample_entity_invalid", sequence);
        ResetImpactState(true);
        return Plugin_Stop;
    }

    float playerOrigin[3];
    float playerVelocity[3];
    float ballOrigin[3];
    float ballVelocity[3];
    GetClientAbsOrigin(probeClient, playerOrigin);
    GetEntPropVector(probeClient, Prop_Data, "m_vecVelocity", playerVelocity);
    GetEntPropVector(impactBall, Prop_Data, "m_vecAbsOrigin", ballOrigin);
    GetEntPropVector(impactBall, Prop_Data, "m_vecVelocity", ballVelocity);

    float playerPlanarSpeed = SquareRoot(
        playerVelocity[0] * playerVelocity[0] + playerVelocity[1] * playerVelocity[1]);
    if (playerPlanarSpeed > g_ImpactMaxPlanarSpeed)
    {
        g_ImpactMaxPlanarSpeed = playerPlanarSpeed;
    }

    g_ImpactSampleIndex++;
    LogMessage(
        "[SM2CSSREF] impact_sample seq=%d n=%d playerOrigin=(%.3f %.3f %.3f) playerVelocity=(%.3f %.3f %.3f) playerPlanarSpeed=%.3f ballOrigin=(%.3f %.3f %.3f) ballVelocity=(%.3f %.3f %.3f) health=%d",
        sequence,
        g_ImpactSampleIndex,
        playerOrigin[0], playerOrigin[1], playerOrigin[2],
        playerVelocity[0], playerVelocity[1], playerVelocity[2],
        playerPlanarSpeed,
        ballOrigin[0], ballOrigin[1], ballOrigin[2],
        ballVelocity[0], ballVelocity[1], ballVelocity[2],
        GetClientHealth(probeClient));

    if (g_ImpactSampleIndex < 75)
    {
        return Plugin_Continue;
    }

    g_ImpactTimer = INVALID_HANDLE;
    LogMessage(
        "[SM2CSSREF] impact_end seq=%d requestedSpeed=%.1f maxPlayerPlanarSpeed=%.3f hurtEvent=%d finalHealth=%d",
        sequence,
        g_ImpactLaunchSpeed,
        g_ImpactMaxPlanarSpeed,
        g_ImpactHurtEvent ? 1 : 0,
        GetClientHealth(probeClient));
    ResetImpactState(true);
    return Plugin_Stop;
}

public Action Timer_StartSettledTrial(Handle timer, DataPack pack)
{
    pack.Reset();
    int sequence = pack.ReadCell();
    int ball = EntRefToEntIndex(g_TrialBallRef);
    if (ball <= MaxClients || !IsValidEntity(ball) || sequence != g_TrialSequence)
    {
        LogMessage("[SM2CSSREF] trial_abort seq=%d reason=settle_ball_invalid", sequence);
        return Plugin_Stop;
    }

    float velocity[3];
    if (StrEqual(g_TrialKind, "roll", false))
    {
        velocity[0] = 400.0;
        g_TrialSampleLimit = 200;
    }
    else
    {
        velocity[0] = -600.0;
        g_TrialSampleLimit = 90;
    }

    TeleportEntity(ball, NULL_VECTOR, NULL_VECTOR, velocity);
    AcceptEntityInput(ball, "Wake");
    StartTrialSampling(sequence, g_TrialSampleLimit);
    return Plugin_Stop;
}

void StartTrialSampling(int sequence, int sampleLimit)
{
    int ball = EntRefToEntIndex(g_TrialBallRef);
    if (ball <= MaxClients || !IsValidEntity(ball))
    {
        LogMessage("[SM2CSSREF] trial_abort seq=%d reason=start_ball_invalid", sequence);
        ResetTrialState(true);
        return;
    }

    g_TrialSampleIndex = 0;
    g_TrialSampleLimit = sampleLimit;
    g_TrialPreviousTime = GetEngineTime();
    GetEntPropVector(ball, Prop_Data, "m_vecAbsOrigin", g_TrialPreviousPosition);
    float requestedVelocity = StrEqual(g_TrialKind, "roll", false) ? 400.0
        : (StrEqual(g_TrialKind, "wall", false) ? -600.0 : 0.0);
    LogMessage(
        "[SM2CSSREF] trial_start seq=%d kind=%s time=%.6f samples=%d interval=%.3f requestedX=%.3f position=(%.6f %.6f %.6f)",
        sequence, g_TrialKind, g_TrialPreviousTime, sampleLimit, TRIAL_SAMPLE_INTERVAL,
        requestedVelocity,
        g_TrialPreviousPosition[0], g_TrialPreviousPosition[1], g_TrialPreviousPosition[2]);
    g_TrialTimer = CreateTimer(TRIAL_SAMPLE_INTERVAL, Timer_SampleTrial, sequence,
        TIMER_REPEAT | TIMER_FLAG_NO_MAPCHANGE);
}

public Action Timer_SampleTrial(Handle timer, any sequence)
{
    int ball = EntRefToEntIndex(g_TrialBallRef);
    if (timer != g_TrialTimer
        || sequence != g_TrialSequence
        || ball <= MaxClients
        || !IsValidEntity(ball))
    {
        if (timer == g_TrialTimer) g_TrialTimer = INVALID_HANDLE;
        LogMessage("[SM2CSSREF] trial_abort seq=%d reason=sample_ball_invalid", sequence);
        return Plugin_Stop;
    }

    float now = GetEngineTime();
    float position[3];
    float angles[3];
    GetEntPropVector(ball, Prop_Data, "m_vecAbsOrigin", position);
    GetEntPropVector(ball, Prop_Data, "m_angAbsRotation", angles);
    float elapsed = now - g_TrialPreviousTime;
    float derived[3];
    if (elapsed > 0.000001)
    {
        derived[0] = (position[0] - g_TrialPreviousPosition[0]) / elapsed;
        derived[1] = (position[1] - g_TrialPreviousPosition[1]) / elapsed;
        derived[2] = (position[2] - g_TrialPreviousPosition[2]) / elapsed;
    }

    g_TrialSampleIndex++;
    LogMessage(
        "[SM2CSSREF] trial_sample seq=%d kind=%s n=%d time=%.6f dt=%.6f position=(%.6f %.6f %.6f) derived=(%.6f %.6f %.6f) speed=%.6f angles=(%.6f %.6f %.6f)",
        sequence, g_TrialKind, g_TrialSampleIndex, now, elapsed,
        position[0], position[1], position[2],
        derived[0], derived[1], derived[2], GetVectorLength(derived),
        angles[0], angles[1], angles[2]);
    g_TrialPreviousTime = now;
    g_TrialPreviousPosition[0] = position[0];
    g_TrialPreviousPosition[1] = position[1];
    g_TrialPreviousPosition[2] = position[2];

    if (g_TrialSampleIndex < g_TrialSampleLimit)
    {
        return Plugin_Continue;
    }

    g_TrialTimer = INVALID_HANDLE;
    LogMessage(
        "[SM2CSSREF] trial_end seq=%d kind=%s samples=%d finalPosition=(%.6f %.6f %.6f)",
        sequence, g_TrialKind, g_TrialSampleIndex,
        position[0], position[1], position[2]);
    int finishedBall = EntRefToEntIndex(g_TrialBallRef);
    g_TrialBallRef = INVALID_ENT_REFERENCE;
    if (finishedBall > MaxClients && IsValidEntity(finishedBall))
    {
        RemoveEntity(finishedBall);
    }
    return Plugin_Stop;
}

void ResetTrialState(bool removeBall)
{
    if (g_TrialTimer != INVALID_HANDLE)
    {
        delete g_TrialTimer;
        g_TrialTimer = INVALID_HANDLE;
    }

    int trialBall = EntRefToEntIndex(g_TrialBallRef);
    g_TrialBallRef = INVALID_ENT_REFERENCE;
    if (removeBall && trialBall > MaxClients && IsValidEntity(trialBall))
    {
        RemoveEntity(trialBall);
    }

    g_TrialSampleIndex = 0;
    g_TrialSampleLimit = 0;
    g_TrialPreviousTime = 0.0;
    g_TrialKind[0] = '\0';
}

void ResetImpactState(bool removeEntities)
{
    if (g_ImpactTimer != INVALID_HANDLE)
    {
        delete g_ImpactTimer;
        g_ImpactTimer = INVALID_HANDLE;
    }

    int impactBall = EntRefToEntIndex(g_ImpactBallRef);
    g_ImpactBallRef = INVALID_ENT_REFERENCE;
    if (removeEntities && impactBall > MaxClients && IsValidEntity(impactBall))
    {
        RemoveEntity(impactBall);
    }

    int probeClient = GetClientOfUserId(g_ImpactClientUserId);
    g_ImpactClientUserId = 0;
    if (removeEntities && probeClient > 0 && IsClientConnected(probeClient))
    {
        KickClient(probeClient, "CS:S impact reference trial complete");
    }

    g_ImpactSampleIndex = 0;
    g_ImpactLaunchSpeed = 0.0;
    g_ImpactMaxPlanarSpeed = 0.0;
    g_ImpactHurtEvent = false;
}
