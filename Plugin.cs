using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using GorillaLocomotion;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace TriggerTurn;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static Plugin? Active;

    private Harmony? harmony;

    private const float TurnSpeed = 90f;
    private const float TriggerThreshold = 0.5f;

    private bool ready;
    private bool sawLeft;
    private bool sawRight;
    private bool confirmedTurn;

    private void OnEnable()
    {
        try
        {
            Active = this;
            harmony = new Harmony(PluginInfo.Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            Logger.LogInfo(
                "TriggerTurn patched successfully. Joystick turning blocked.");
        }
        catch (Exception error)
        {
            harmony?.UnpatchSelf();
            Active = null;

            Logger.LogError(
                $"TriggerTurn patch failed. Normal turning restored.\n{error}");
        }
    }

    private void OnDisable()
    {
        harmony?.UnpatchSelf();

        if (Active == this)
            Active = null;
    }

    internal float GetTurn(HashSet<ISnapTurnOverride> overrides)
    {
        var input = ControllerInputPoller.instance;
        var player = GTPlayer.Instance;

        if (input == null ||
            player == null ||
            player.headCollider == null ||
            player.turnParent == null)
        {
            return 0f;
        }

        if (!ready)
        {
            ready = true;
            Logger.LogInfo("TriggerTurn ready.");
        }

        bool left =
            input.leftControllerIndexFloat > TriggerThreshold;

        bool right =
            input.rightControllerIndexFloat > TriggerThreshold;

        if (left && !sawLeft)
        {
            sawLeft = true;
            Logger.LogInfo("Left trigger detected.");
        }

        if (right && !sawRight)
        {
            sawRight = true;
            Logger.LogInfo("Right trigger detected.");
        }

        // Respect gameplay that temporarily takes control of turning.
        if (overrides.Count > 0)
            return 0f;

        // Both or neither trigger pressed: stop turning.
        if (left == right)
            return 0f;

        float direction = right ? 1f : -1f;

        return direction *
               TurnSpeed *
               Mathf.Min(Time.deltaTime, 0.05f);
    }

    internal void ConfirmTurn(float degrees)
    {
        if (confirmedTurn || degrees == 0f)
            return;

        confirmedTurn = true;
        Logger.LogInfo(
            "TriggerTurn applied: native player turning executed.");
    }
}

[HarmonyPatch(typeof(GorillaSnapTurn), nameof(GorillaSnapTurn.Tick))]
internal static class TurnPatch
{
    internal static bool ApplyingTriggerTurn;

    private static void IgnoreJoystickTurn(
        GorillaSnapTurn provider,
        float amount)
    {
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);

        var startTurn = AccessTools.DeclaredMethod(
            typeof(GorillaSnapTurn),
            "StartTurn",
            new[] { typeof(float) });

        var ignore = AccessTools.DeclaredMethod(
            typeof(TurnPatch),
            nameof(IgnoreJoystickTurn));

        int matches = 0;

        foreach (var instruction in code)
        {
            if (!instruction.Calls(startTurn))
                continue;

            instruction.opcode = OpCodes.Call;
            instruction.operand = ignore;
            matches++;
        }

        // These two call sites were verified in your installed game.
        if (matches != 2)
        {
            throw new InvalidOperationException(
                $"Expected 2 joystick turn calls; found {matches}.");
        }

        return code;
    }

    private static void Prefix(
        ref float ___m_CurrentTurnAmount,
        HashSet<ISnapTurnOverride> ___turningOverriders,
        out bool __state)
    {
        // Preserve turns already queued by scripted gameplay.
        __state =
            Plugin.Active != null &&
            ___m_CurrentTurnAmount == 0f;

        if (__state)
        {
            ___m_CurrentTurnAmount =
                Plugin.Active!.GetTurn(___turningOverriders);
        }

        ApplyingTriggerTurn =
            __state && ___m_CurrentTurnAmount != 0f;
    }

    private static void Finalizer(
        ref float ___m_CurrentTurnAmount,
        bool __state)
    {
        // Prevent a delayed turn if native locomotion was blocked.
        if (__state)
            ___m_CurrentTurnAmount = 0f;

        ApplyingTriggerTurn = false;
    }
}

[HarmonyPatch(
    typeof(GTPlayer),
    nameof(GTPlayer.Turn),
    new[] { typeof(float) })]
internal static class AppliedTurnPatch
{
    private static void Postfix(float __0)
    {
        if (TurnPatch.ApplyingTriggerTurn)
            Plugin.Active?.ConfirmTurn(__0);
    }
}