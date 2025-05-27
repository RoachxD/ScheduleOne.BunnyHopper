using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using BunnyHopper.Models;

#if IL2CPP_BUILD
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Settings;
using IEnumerator = Il2CppSystem.Collections.IEnumerator;
using PlayerMovementType = Il2CppScheduleOne.PlayerScripts.PlayerMovement;
#elif MONO_BUILD
using ScheduleOne.UI;
using ScheduleOne.UI.Settings;
using IEnumerator = System.Collections.IEnumerator;
using PlayerMovementType = ScheduleOne.PlayerScripts.PlayerMovement;
#endif

namespace BunnyHopper;

public class Main : MelonMod
{
    public static MelonPreferences_Category SettingsCategory { get; private set; }
    public static MelonPreferences_Entry<bool> Enabled { get; private set; }
    public static MelonPreferences_Entry<int> AutoJumpLiftoffTimeoutMilliseconds
    {
        get;
        private set;
    }
    private static InputActionReference jumpActionReference = null;

    private static readonly Dictionary<int, PlayerJumpState> playerJumpStates = new();

    public override void OnInitializeMelon()
    {
        SettingsCategory = MelonPreferences.CreateCategory(
            "BunnyHopper_Settings",
            "Bunny Hopper Settings"
        );
        Enabled = SettingsCategory.CreateEntry(
            "Enabled",
            true,
            "Enable or disable the bunny hop feature."
        );
        AutoJumpLiftoffTimeoutMilliseconds = SettingsCategory.CreateEntry(
            "AutoJumpLiftoffTimeout",
            75,
            "Liftoff timeout (ms) before auto-jump is considered failed.",
            validator: new MelonLoader.Preferences.ValueRange<int>(55, 110)
        );

#if IL2CPP_BUILD
        string buildType = "IL2CPP";
#elif MONO_BUILD
        string buildType = "Mono";
#else
        string buildType = "Unknown";
#endif
        Melon<Main>.Logger.Msg(
            $"Bunny Hopper ({buildType}) initializing with following settings: Enabled={Enabled.Value}, AutoJumpLiftoffTimeoutMilliseconds={AutoJumpLiftoffTimeoutMilliseconds.Value}."
        );

        // Apply all patches in this assembly
        try
        {
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            var patchedMethods = HarmonyInstance
                .GetPatchedMethods()
                .Select(p => $"{p.DeclaringType.FullName}.{p.Name}");
            string patchesInfo = string.Join(", ", patchedMethods);
            Melon<Main>.Logger.Msg($"Harmony patches successfully applied: {patchesInfo}.");
        }
        catch (Exception ex)
        {
            Melon<Main>.Logger.Error($"Error during Harmony's PatchAll execution: {ex}");
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        playerJumpStates.Clear();
        if (!Enabled.Value || jumpActionReference != null)
        {
            return;
        }

        if (sceneName != "Menu")
        {
            return;
        }

        Melon<Main>.Logger.Msg(
            "Main menu was loaded, attempting to detect Jump Action reference.."
        );
        DetectJumpAction();
    }

    private static void DetectJumpAction()
    {
        var mainMenu = GameObject.Find("MainMenu");
        if (mainMenu == null)
        {
            Melon<Main>.Logger.Error("Main Menu not found, unable to detect Jump Action!");
            return;
        }

        Transform jumpControl = mainMenu.transform.Find("Settings/Content/Controls/Row/Jump");
        if (jumpControl == null)
        {
            Melon<Main>.Logger.Error(
                "Jump control UI element not found, unable to detect Jump Action!"
            );
            return;
        }

        var keybinder = jumpControl.GetComponent<Keybinder>();
        if (keybinder == null)
        {
            Melon<Main>.Logger.Error(
                "Keybinder component not found on Jump control, unable to detect Jump Action!"
            );
            return;
        }

        var rebindUI = keybinder.rebindActionUI;
        if (rebindUI == null)
        {
            Melon<Main>.Logger.Error(
                "Rebind Action UI is null on Keybinder component, unable to detect Jump Action!"
            );
            return;
        }

        var actionRef = rebindUI.actionReference;
        if (actionRef == null)
        {
            Melon<Main>.Logger.Error("Jump Action reference is null on Rebind Action UI!");
            return;
        }

        jumpActionReference = actionRef;
        Melon<Main>.Logger.Msg(
            $"Successfully auto-detected Jump Action: Name={jumpActionReference.action.name}, ID={jumpActionReference.action.id}."
        );
    }

    [HarmonyPatch(typeof(PlayerMovementType), "Move")]
    private static class PlayerMovementMovePatch
    {
        private static MethodInfo _jumpMethodInfo;
        private static MemberInfo _isJumpingMemberInfo; // Could be FieldInfo or PropertyInfo

        [SuppressMessage(
            "csharpsquid",
            "S3011",
            Justification = "Using reflection to access private/protected members of PlayerMovement."
        )]
        static PlayerMovementMovePatch()
        {
            // Initialize reflection members for Jump method
            _jumpMethodInfo = typeof(PlayerMovementType).GetMethod(
                "Jump",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (_jumpMethodInfo == null)
            {
                Melon<Main>.Logger.Error(
                    "Reflection: PlayerMovement.Jump method not found. Auto-jumping will not function."
                );
            }

            // Initialize reflection members for isJumping field or property
            _isJumpingMemberInfo =
                (MemberInfo)
                    typeof(PlayerMovementType).GetField(
                        "isJumping",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                    )
                ?? typeof(PlayerMovementType).GetProperty(
                    "isJumping",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );

            if (_isJumpingMemberInfo == null)
            {
                Melon<Main>.Logger.Error(
                    "Reflection: PlayerMovement.isJumping member (field/property) not found. Auto-jump condition check might be impaired."
                );
            }
        }

        [SuppressMessage(
            "csharpsquid",
            "S1144",
            Justification = "Harmony patch method, used by reflection."
        )]
        public static void Prefix(PlayerMovementType __instance)
        {
            if (ShouldSkipPatch(__instance))
                return;

            int instanceId = __instance.GetInstanceID();
            if (!playerJumpStates.ContainsKey(instanceId))
            {
                // Ensure playerJumpStates has an entry for the instance.
                playerJumpStates[instanceId] = new PlayerJumpState();
            }

            playerJumpStates[instanceId].WasGroundedBeforeMove = __instance.Controller.isGrounded;
        }

        [SuppressMessage(
            "csharpsquid",
            "S1144",
            Justification = "Harmony patch method, used by reflection."
        )]
        public static void Postfix(PlayerMovementType __instance)
        {
            if (ShouldSkipPatch(__instance))
                return;

            int instanceId = __instance.GetInstanceID();
            if (!playerJumpStates.TryGetValue(instanceId, out PlayerJumpState jumpState))
            {
                // This should not happen if Prefix ran correctly. Log an error and abort.
                Melon<Main>.Logger.Error(
                    $"[Move PF ID {instanceId}] Critical: PlayerJumpState not found or null after Prefix. Aborting Postfix logic."
                );
                return;
            }

            bool currentControllerIsGrounded = __instance.Controller.isGrounded;

            // Check if we were awaiting liftoff from a mod-initiated auto-jump.
            if (jumpState.AwaitingLiftoffAfterAutoJump)
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[Move PF ID {instanceId}] Awaiting liftoff. Controller.isGrounded: {currentControllerIsGrounded}, PM.IsGrounded: {__instance.IsGrounded}"
                );
#endif
                // Liftoff achieved based on CharacterController.
                if (!currentControllerIsGrounded)
                {
#if DEBUG
                    Melon<Main>.Logger.Msg(
                        $"[Move PF ID {instanceId}] Liftoff confirmed by Controller.isGrounded. Updating jumpState."
                    );
#endif
                    jumpState.AwaitingLiftoffAfterAutoJump = false;
                    jumpState.ModInitiatedCurrentJump = true; // Mark that the current jump (now airborne) was mod-initiated
                }
                // else: still awaiting liftoff, Controller is still grounded.
                return; // If awaiting liftoff, do nothing else this frame (no landing checks, no new auto-jump).
            }

            // Standard logic for when not awaiting liftoff (e.g., regular movement, landing).
            bool wasGroundedPrevController = jumpState.WasGroundedBeforeMove;
            bool modDidInitiateThisJump = jumpState.ModInitiatedCurrentJump;

            // Landing condition: was not grounded before Move, is grounded now.
            if (!wasGroundedPrevController && currentControllerIsGrounded)
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[Move PF ID {instanceId}] Just Landed (Controller state). ModInitiatedThisJump: {modDidInitiateThisJump}"
                );
#endif
                if (modDidInitiateThisJump)
                {
                    // Reset flag, this mod-initiated jump has now landed.
                    jumpState.ModInitiatedCurrentJump = false;
                }

                // Check if we should initiate a new auto-jump upon landing.
                if (ShouldAutoJump(__instance))
                {
#if DEBUG
                    Melon<Main>.Logger.Msg(
                        $"[Move PF ID {instanceId}] Conditions met for auto-jump. Starting ExecuteAutoJumpCoroutine."
                    );
#endif
                    MelonCoroutines.Start(ExecuteAutoJumpCoroutine(__instance, jumpState));
                }
            }
        }

        private static System.Collections.IEnumerator ExecuteAutoJumpCoroutine(
            PlayerMovementType playerMovement,
            PlayerJumpState jumpState
        )
        {
            if (playerMovement == null || playerMovement.Controller == null)
                yield break;

            int instanceId = playerMovement.GetInstanceID();

            if (jumpState.AwaitingLiftoffAfterAutoJump)
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[ExecuteAutoJumpCoro ID {instanceId}] Already awaiting liftoff. Aborting new coroutine."
                );
#endif
                yield break;
            }

            jumpState.AwaitingLiftoffAfterAutoJump = true;
            jumpState.ModInitiatedCurrentJump = false;
#if DEBUG
            Melon<Main>.Logger.Msg(
                $"[ExecuteAutoJumpCoro ID {instanceId}] Set AwaitingLiftoffAfterAutoJump. Will attempt jump after 1 frame delay."
            );
#endif

            yield return null; // Wait one frame for game state to settle (e.g., input, physics)

            // Re-check conditions after frame delay
            if (ShouldAutoJump(playerMovement))
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[ExecuteAutoJumpCoro ID {instanceId}] Conditions met after delay. Performing Jump."
                );
#endif
                var jumpEnumerator = _jumpMethodInfo.Invoke(playerMovement, null) as IEnumerator;
                playerMovement.StartCoroutine(jumpEnumerator);
                MelonCoroutines.Start(MonitorLiftoff(playerMovement, jumpState));
            }
            else
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[ExecuteAutoJumpCoro ID {instanceId}] Conditions NOT met after delay. Resetting AwaitingLiftoffAfterAutoJump."
                );
#endif
                jumpState.AwaitingLiftoffAfterAutoJump = false;
            }
        }

        private static System.Collections.IEnumerator MonitorLiftoff(
            PlayerMovementType pm,
            PlayerJumpState jumpState
        )
        {
            int instanceId = pm.GetInstanceID();
            float timeoutSeconds = AutoJumpLiftoffTimeoutMilliseconds.Value / 1000.0f;
            float endTime = Time.time + timeoutSeconds;

#if DEBUG
            Melon<Main>.Logger.Msg(
                $"[MonitorLiftoff ID {instanceId}] Started. Timeout: {AutoJumpLiftoffTimeoutMilliseconds.Value}ms. Will check Controller.isGrounded if timeout occurs."
            );
#endif

            while (Time.time < endTime)
            {
                // Player or controller gone
                if (pm == null || pm.Controller == null)
                {
                    yield break;
                }

                // Check if Postfix has already confirmed liftoff and cleared the flag
                if (!jumpState.AwaitingLiftoffAfterAutoJump)
                {
#if DEBUG
                    Melon<Main>.Logger.Msg(
                        $"[MonitorLiftoff ID {instanceId}] AwaitingLiftoffAfterAutoJump flag cleared by Postfix (liftoff success). Exiting monitor."
                    );
#endif
                    yield break;
                }
                yield return null; // Check each frame
            }

            // Timeout has expired. Check if still awaiting liftoff.
            if (pm != null && pm.Controller != null && jumpState.AwaitingLiftoffAfterAutoJump)
            {
#if DEBUG
                Melon<Main>.Logger.Warning(
                    $"[MonitorLiftoff ID {instanceId}] Timeout! AwaitingLiftoffAfterAutoJump still true."
                );
#endif
                // Final check using Controller.isGrounded
                if (pm.Controller.isGrounded)
                {
#if DEBUG
                    Melon<Main>.Logger.Warning(
                        $"[MonitorLiftoff ID {instanceId}] Player still Controller.isGrounded after timeout. Resetting AwaitingLiftoffAfterAutoJump. Jump likely failed or was suppressed."
                    );
#endif
                    jumpState.AwaitingLiftoffAfterAutoJump = false;
                }
                else // This indicates a potential issue where Postfix didn't clear the flag despite liftoff.
                {
#if DEBUG
                    Melon<Main>.Logger.Warning(
                        $"[MonitorLiftoff ID {instanceId}] Player is NOT Controller.isGrounded after timeout, but awaiting flag was not cleared by Postfix. Clearing it now."
                    );
#endif
                    jumpState.AwaitingLiftoffAfterAutoJump = false;
                }
            }
#if DEBUG
            else
                Melon<Main>.Logger.Msg(
                    $"[MonitorLiftoff ID {instanceId}] Timeout expired, but awaiting flag was already false or player/controller gone."
                );
#endif
        }

        private static bool ShouldAutoJump(PlayerMovementType pm)
        {
            if (
                !jumpActionReference.action.IsPressed()
                || !(pm.IsGrounded || pm.Controller.isGrounded) // Player is grounded if EITHER PM or Controller says so
                || !pm.canJump
            )
            {
                return false;
            }

            try
            {
                if (_isJumpingMemberInfo is FieldInfo fieldInfo)
                {
                    return (bool)fieldInfo.GetValue(pm);
                }
                else if (_isJumpingMemberInfo is PropertyInfo propertyInfo)
                {
                    return (bool)propertyInfo.GetValue(pm);
                }
            }
            catch (Exception ex)
            {
                Melon<Main>.Logger.Error(
                    $"Error getting PlayerMovement.isJumping via reflection: {ex.Message}"
                );
            }
            return false;
        }

        private static bool ShouldSkipPatch(PlayerMovementType __instance) =>
            !Enabled.Value
            || _jumpMethodInfo == null
            || _isJumpingMemberInfo == null
            || jumpActionReference == null
            || jumpActionReference.action == null
            || __instance == null
            || __instance.Controller == null;
    }
}
