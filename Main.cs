using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using BunnyHopper.Models;

#if IL2CPP_BUILD
using Il2CppScheduleOne.UI.Settings;
using IEnumerator = Il2CppSystem.Collections.IEnumerator;
using PlayerMovementType = Il2CppScheduleOne.PlayerScripts.PlayerMovement;
#elif MONO_BUILD
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

    private static PlayerJumpState currentPlayerJumpState;

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
        private static Func<PlayerMovementType, IEnumerator> _invokeJumpMethod;
        private static Func<PlayerMovementType, bool> _getIsJumpingValue;

        [SuppressMessage(
            "csharpsquid",
            "S3011",
            Justification = "Using reflection to access private/protected members of PlayerMovement."
        )]
        static PlayerMovementMovePatch()
        {
            var playerMovementType = typeof(PlayerMovementType);
            // Initialize reflection members for Jump method
            MethodInfo jumpMethod = playerMovementType.GetMethod(
                "Jump",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (jumpMethod != null)
            {
                try
                {
                    var param = Expression.Parameter(playerMovementType, "playerMovement");
                    var call = Expression.Call(param, jumpMethod);
                    var lambda = Expression.Lambda<Func<PlayerMovementType, IEnumerator>>(
                        call,
                        param
                    );
                    _invokeJumpMethod = lambda.Compile();
                }
                catch (Exception ex)
                {
                    Melon<Main>.Logger.Error(
                        $"Failed to compile delegate for Jump method: {ex.Message}. Auto-jumping will not function."
                    );
                }
            }
            else
            {
                Melon<Main>.Logger.Error(
                    "Reflection: PlayerMovement.Jump method not found. Auto-jumping will not function."
                );
            }

            // Initialize reflection members for isJumping field or property
            MemberInfo isJumpingMember =
                (MemberInfo)
                    typeof(PlayerMovementType).GetField(
                        "isJumping",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                    )
                ?? typeof(PlayerMovementType).GetProperty(
                    "isJumping",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );

            if (isJumpingMember != null)
            {
                try
                {
                    var param = Expression.Parameter(playerMovementType, "playerMovement");
                    Expression memberAccess = Expression.MakeMemberAccess(param, isJumpingMember);
                    var lambda = Expression.Lambda<Func<PlayerMovementType, bool>>(
                        memberAccess,
                        param
                    );
                    _getIsJumpingValue = lambda.Compile();
                }
                catch (Exception ex)
                {
                    Melon<Main>.Logger.Error(
                        $"Failed to compile delegate for isJumping member: {ex.Message}. Auto-jump condition check might be impaired."
                    );
                }
            }
            else
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

            currentPlayerJumpState ??= new PlayerJumpState();
            currentPlayerJumpState.WasGroundedBeforeMove = __instance.Controller.isGrounded;
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

            if (currentPlayerJumpState == null)
            {
                Melon<Main>.Logger.Error(
                    $"[PlayerMovement.Move.Postfix] Critical: currentPlayerJumpState is null. Aborting Postfix logic."
                );
                return;
            }

            bool currentControllerIsGrounded = __instance.Controller.isGrounded;

            // Check if we were awaiting liftoff from a mod-initiated auto-jump.
            if (currentPlayerJumpState.AwaitingLiftoffAfterAutoJump)
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[PlayerMovement.Move.Postfix] Awaiting liftoff. Controller.isGrounded: {currentControllerIsGrounded}, PM.IsGrounded: {__instance.IsGrounded}"
                );
#endif
                // Liftoff achieved based on CharacterController.
                if (!currentControllerIsGrounded)
                {
#if DEBUG
                    Melon<Main>.Logger.Msg(
                        $"[PlayerMovement.Move.Postfix] Liftoff confirmed by Controller.isGrounded. Updating jumpState."
                    );
#endif
                    currentPlayerJumpState.AwaitingLiftoffAfterAutoJump = false;
                    currentPlayerJumpState.ModInitiatedCurrentJump = true; // Mark that the current jump (now airborne) was mod-initiated
                }
                // else: still awaiting liftoff, Controller is still grounded.
                return; // If awaiting liftoff, do nothing else this frame (no landing checks, no new auto-jump).
            }

            // Standard logic for when not awaiting liftoff (e.g., regular movement, landing).
            bool wasGroundedPrevController = currentPlayerJumpState.WasGroundedBeforeMove;
            bool modDidInitiateThisJump = currentPlayerJumpState.ModInitiatedCurrentJump;

            // Landing condition: was not grounded before Move, is grounded now.
            if (!wasGroundedPrevController && currentControllerIsGrounded)
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[PlayerMovement.Move.Postfix] Just Landed (Controller state). ModInitiatedThisJump: {modDidInitiateThisJump}"
                );
#endif
                if (modDidInitiateThisJump)
                {
                    // Reset flag, this mod-initiated jump has now landed.
                    currentPlayerJumpState.ModInitiatedCurrentJump = false;
                }

                // Check if we should initiate a new auto-jump upon landing.
                if (ShouldAutoJump(__instance))
                {
#if DEBUG
                    Melon<Main>.Logger.Msg(
                        $"[PlayerMovement.Move.Postfix] Conditions met for auto-jump. Starting ExecuteAutoJumpCoroutine."
                    );
#endif
                    MelonCoroutines.Start(
                        ExecuteAutoJumpCoroutine(__instance, currentPlayerJumpState)
                    );
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

            if (jumpState.AwaitingLiftoffAfterAutoJump)
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[ExecuteAutoJumpCoro] Already awaiting liftoff. Aborting new coroutine."
                );
#endif
                yield break;
            }

            jumpState.AwaitingLiftoffAfterAutoJump = true;
            jumpState.ModInitiatedCurrentJump = false;
#if DEBUG
            Melon<Main>.Logger.Msg(
                $"[ExecuteAutoJumpCoro] Set AwaitingLiftoffAfterAutoJump. Will attempt jump after 1 frame delay."
            );
#endif

            yield return null; // Wait one frame for game state to settle (e.g., input, physics)

            // Re-check conditions after frame delay
            if (ShouldAutoJump(playerMovement))
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[ExecuteAutoJumpCoro] Conditions met after delay. Performing Jump."
                );
#endif
                if (_invokeJumpMethod != null)
                {
                    var jumpEnumerator = _invokeJumpMethod(playerMovement); // Use compiled delegate
                    playerMovement.StartCoroutine(jumpEnumerator);
                    MelonCoroutines.Start(MonitorLiftoff(playerMovement, jumpState));
                }
                else
                {
                    Melon<Main>.Logger.Error(
                        "[ExecuteAutoJumpCoro] Jump method delegate is null. Cannot execute jump."
                    );
                    jumpState.AwaitingLiftoffAfterAutoJump = false; // Reset state
                }
            }
            else
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[ExecuteAutoJumpCoro] Conditions NOT met after delay. Resetting AwaitingLiftoffAfterAutoJump."
                );
#endif
                jumpState.AwaitingLiftoffAfterAutoJump = false;
            }
        }

        private static System.Collections.IEnumerator MonitorLiftoff(
            PlayerMovementType playerMovement,
            PlayerJumpState jumpState
        )
        {
            float timeoutSeconds = AutoJumpLiftoffTimeoutMilliseconds.Value / 1000.0f;
            float endTime = Time.time + timeoutSeconds;

#if DEBUG
            Melon<Main>.Logger.Msg(
                $"[MonitorLiftoff] Started. Timeout: {AutoJumpLiftoffTimeoutMilliseconds.Value}ms. Will check Controller.isGrounded if timeout occurs."
            );
#endif

            while (Time.time < endTime)
            {
                // Player or controller gone
                if (playerMovement == null || playerMovement.Controller == null)
                {
                    yield break;
                }

                // Check if Postfix has already confirmed liftoff and cleared the flag
                if (!jumpState.AwaitingLiftoffAfterAutoJump)
                {
#if DEBUG
                    Melon<Main>.Logger.Msg(
                        $"[MonitorLiftoff] AwaitingLiftoffAfterAutoJump flag cleared by Postfix (liftoff success). Exiting monitor."
                    );
#endif
                    yield break;
                }
                yield return null; // Check each frame
            }

            // Timeout has expired. Check if still awaiting liftoff.
            if (
                playerMovement != null
                && playerMovement.Controller != null
                && jumpState.AwaitingLiftoffAfterAutoJump
            )
            {
#if DEBUG
                Melon<Main>.Logger.Warning(
                    $"[MonitorLiftoff] Timeout! AwaitingLiftoffAfterAutoJump still true."
                );
#endif
                // Final check using Controller.isGrounded
                if (playerMovement.Controller.isGrounded)
                {
#if DEBUG
                    Melon<Main>.Logger.Warning(
                        $"[MonitorLiftoff] Player still Controller.isGrounded after timeout. Resetting AwaitingLiftoffAfterAutoJump. Jump likely failed or was suppressed."
                    );
#endif
                    jumpState.AwaitingLiftoffAfterAutoJump = false;
                }
                else // This indicates a potential issue where Postfix didn't clear the flag despite liftoff.
                {
#if DEBUG
                    Melon<Main>.Logger.Warning(
                        $"[MonitorLiftoff] Player is NOT Controller.isGrounded after timeout, but awaiting flag was not cleared by Postfix. Clearing it now."
                    );
#endif
                    jumpState.AwaitingLiftoffAfterAutoJump = false;
                }
            }
#if DEBUG
            else
                Melon<Main>.Logger.Msg(
                    $"[MonitorLiftoff] Timeout expired, but awaiting flag was already false or player/controller gone."
                );
#endif
        }

        private static bool ShouldAutoJump(PlayerMovementType playerMovement)
        {
            if (
                !jumpActionReference.action.IsPressed()
                || !(playerMovement.IsGrounded || playerMovement.Controller.isGrounded) // Player is grounded if EITHER PM or Controller says so
                || !playerMovement.canJump
            )
            {
#if DEBUG
                Melon<Main>.Logger.Msg(
                    $"[ShouldAutoJump] Conditions not met for auto-jump. Jump Action pressed: {jumpActionReference.action.IsPressed()}, IsGrounded: {playerMovement.IsGrounded}, Controller.isGrounded: {playerMovement.Controller.isGrounded}, canJump: {playerMovement.canJump}"
                );
#endif
                return false;
            }

            try
            {
                return !_getIsJumpingValue(playerMovement);
            }
            catch (Exception ex)
            {
                Melon<Main>.Logger.Error(
                    $"Error getting PlayerMovement.isJumping via reflection: {ex.Message}"
                );
            }
            return false;
        }

        private static bool ShouldSkipPatch(PlayerMovementType playerMovement) =>
            !Enabled.Value
            || _invokeJumpMethod == null
            || _getIsJumpingValue == null
            || jumpActionReference == null
            || jumpActionReference.action == null
            || playerMovement == null
            || playerMovement.Controller == null;
    }
}
