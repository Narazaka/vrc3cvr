using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI_RC.Core.EventSystem;
using ABI_RC.Core.Player;
using ABI_RC.Systems.Communications;
using ABI_RC.Systems.InputManagement;
using ABI_RC.Systems.Movement;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Animations;

[assembly: MelonInfo(typeof(VRC3CVRVerification.VerificationMod), "VRC3CVRVerification", "0.1.0", "vrc3cvr")]
// company/product as reported by the game itself (ChilloutVR_Data/app.info)
[assembly: MelonGame("ChilloutVR", "ChilloutVR")]

namespace VRC3CVRVerification
{
    // Machine-verifies the vrc3cvr conversion in game, using the "VRC3CVR Verification Avatar".
    // Wear the converted avatar, then the checks run automatically (or press F9 to rerun).
    // Results land in <game>/VRC3CVR_VerificationReport.txt as PASS/FAIL lines.
    public class VerificationMod : MelonMod
    {
        const string ReportPath = "VRC3CVR_VerificationReport.txt";
        const float PositionTolerance = 0.03f;
        const float AngleToleranceDegrees = 8f;
        // Content ids of the avatars uploaded by the editor-side tooling. They identify someone's
        // personal CVR content, so they live in a git-ignored file (Tests/VerificationAvatarIds.txt
        // in the repository) — copy it next to the game: "fold=<id>" / "derived=<id>", one per line.
        const string IdFilePath = "VerificationAvatarIds.txt";
        // set to run both avatars unattended right after the game starts
        static readonly bool AutoRunSuite = File.Exists("VRC3CVR_AutoVerify.flag");

        static string ReadId(string key)
        {
            if (!File.Exists(IdFilePath))
            {
                return null;
            }
            foreach (var line in File.ReadAllLines(IdFilePath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#") || !trimmed.StartsWith(key + "="))
                {
                    continue;
                }
                var value = trimmed.Substring(key.Length + 1).Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }
            return null;
        }

        bool _running;
        bool _ranForCurrentAvatar;
        bool _suiteStarted;
        readonly List<string> _report = new List<string>();
        float _gestureOverride = float.NaN;

        // The input manager rewrites movementVector every frame, so the injected walk has to be
        // applied after it runs; a postfix on its update is the only ordering that holds.
        internal static Vector3 MovementOverride = Vector3.zero;
        internal static bool InjectMovement;

        public override void OnInitializeMelon()
        {
            var updateInput = typeof(CVRInputManager).GetMethod("UpdateInput",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (updateInput != null)
            {
                HarmonyInstance.Patch(updateInput, postfix: new HarmonyMethod(typeof(VerificationMod).GetMethod(
                    nameof(ForceMovement), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));
                MelonLogger.Msg("patched CVRInputManager.UpdateInput for movement injection");
            }
            else
            {
                MelonLogger.Warning("CVRInputManager.UpdateInput not found; movement injection is unavailable");
            }
        }

        static void ForceMovement(CVRInputManager __instance)
        {
            if (InjectMovement)
            {
                __instance.movementVector = MovementOverride;
            }
        }

        public override void OnUpdate()
        {
            // keep the injected gesture pinned against the input system writing it every frame
            if (!float.IsNaN(_gestureOverride) && PlayerSetup.Instance != null)
            {
                PlayerSetup.Instance.SetGestureLeft(_gestureOverride);
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                _ranForCurrentAvatar = false;
            }
            if (Input.GetKeyDown(KeyCode.F10) && !_suiteStarted)
            {
                _suiteStarted = true;
                MelonCoroutines.Start(RunSuite());
            }

            if (AutoRunSuite && !_suiteStarted && PlayerSetup.Instance != null && PlayerSetup.Instance.IsAvatarLoaded)
            {
                _suiteStarted = true;
                MelonCoroutines.Start(RunSuite());
            }

            if (_running || _ranForCurrentAvatar || _suiteStarted)
            {
                return;
            }
            var avatar = PlayerSetup.Instance != null ? PlayerSetup.Instance.AvatarObject : null;
            if (avatar == null || avatar.transform.Find("Constraints/AnimC") == null)
            {
                return;
            }
            _ranForCurrentAvatar = true;
            MelonCoroutines.Start(RunChecks(avatar.transform));
        }

        // Unattended run: switch to each verification avatar in turn and check it.
        // Results are appended per avatar so a hang in a later run cannot lose earlier ones.
        IEnumerator RunSuite()
        {
            File.WriteAllText(ReportPath, "INFO suite started " + DateTime.Now.ToString("HH:mm:ss") + "\n");
            Step("suite starting; waiting 10s for the world to settle");
            yield return new WaitForSeconds(10f);

            var avatars = new[] { ("Fold", ReadId("fold")), ("Derived", ReadId("derived")) };
            var unconfigured = avatars.Where(entry => string.IsNullOrEmpty(entry.Item2)).ToArray();
            if (unconfigured.Length > 0)
            {
                var missing = string.Join(", ", unconfigured.Select(entry => entry.Item1.ToLower()));
                File.AppendAllText(ReportPath, "FAIL no content id for " + missing + " in " +
                    Path.GetFullPath(IdFilePath) + " (write \"fold=<id>\" / \"derived=<id>\", one per line)\n");
                Step("missing avatar ids in " + Path.GetFullPath(IdFilePath));
            }

            foreach (var entry in avatars.Where(entry => !string.IsNullOrEmpty(entry.Item2)))
            {
                Step("switching to the " + entry.Item1 + " avatar (" + entry.Item2 + ")");
                AssetManagement.Instance.LoadLocalAvatar(entry.Item2);
                // the switch is asynchronous and the previously worn avatar also carries the
                // verification rig, so wait for one whose object name carries this content id
                var waited = 0f;
                var lastLogged = 0f;
                while (waited < 60f)
                {
                    var loaded = PlayerSetup.Instance != null ? PlayerSetup.Instance.AvatarObject : null;
                    if (loaded != null && loaded.name.Contains(entry.Item2) && loaded.transform.Find("Constraints/AnimC") != null)
                    {
                        break;
                    }
                    waited += Time.deltaTime;
                    if (waited - lastLogged >= 5f)
                    {
                        lastLogged = waited;
                        var name = loaded != null ? loaded.name : "(none)";
                        Step("  still waiting for " + entry.Item1 + " (" + waited.ToString("0") + "s, currently \"" + name + "\")");
                    }
                    yield return null;
                }
                var avatar = PlayerSetup.Instance != null ? PlayerSetup.Instance.AvatarObject : null;
                if (avatar == null || !avatar.name.Contains(entry.Item2) || avatar.transform.Find("Constraints/AnimC") == null)
                {
                    File.AppendAllText(ReportPath, "FAIL " + entry.Item1 + ": verification avatar did not load within 60s (worn: \"" + (avatar != null ? avatar.name : "none") + "\")\n");
                    Step(entry.Item1 + ": avatar did not load, skipping");
                    continue;
                }
                // give the avatar a moment to finish initializing after it appears
                yield return new WaitForSeconds(2f);
                Step(entry.Item1 + ": avatar loaded, running checks");
                File.AppendAllText(ReportPath, "INFO ===== " + entry.Item1 + " mode (" + entry.Item2 + ") =====\n");
                yield return RunChecks(avatar.transform);
                Step(entry.Item1 + ": checks finished");
            }

            File.AppendAllText(ReportPath, "INFO done " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            Step("suite done; report at " + Path.GetFullPath(ReportPath));
        }

        // progress marker: goes to the MelonLoader console so a hang is visible from outside
        void Step(string message)
        {
            MelonLogger.Msg("[step] " + message);
        }

        // A coroutine cannot try/catch across a yield, and an exception inside one dies silently.
        // Every synchronous block runs through here so a failure is reported instead of hanging.
        bool Run(string label, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception exception)
            {
                _report.Add("FAIL " + label + " threw " + exception.GetType().Name + ": " + exception.Message);
                MelonLogger.Error("[step] " + label + " threw: " + exception);
                return false;
            }
        }

        IEnumerator RunChecks(Transform avatar)
        {
            _running = true;
            _report.Clear();
            Note("verification started for " + avatar.name);

            // settle after avatar load
            Step("  settling (3s)");
            yield return new WaitForSeconds(3f);

            // the animator may live on the avatar root, on a child, or be tracked by the
            // animator manager; it can also appear a moment after the object does
            Animator animator = null;
            for (var attempt = 0; attempt < 20 && animator == null; attempt++)
            {
                Run("resolve animator", () => animator = avatar.GetComponent<Animator>());
                if (animator == null)
                {
                    Run("resolve animator (children)", () => animator = avatar.GetComponentInChildren<Animator>(true));
                }
                if (animator == null)
                {
                    Run("resolve animator (manager)", () =>
                    {
                        var setup = PlayerSetup.Instance;
                        var manager = setup != null ? setup.AnimatorManager : null;
                        if (manager != null)
                        {
                            animator = manager.Animator;
                        }
                    });
                }
                if (animator == null)
                {
                    yield return new WaitForSeconds(0.5f);
                }
            }
            if (animator == null)
            {
                Check(false, "no Animator found on the avatar; remaining checks skipped");
                Run("describe avatar", () =>
                {
                    var components = string.Join(", ", avatar.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name));
                    Note("avatar root \"" + avatar.name + "\" components: " + components);
                    var children = string.Join(", ", Enumerable.Range(0, avatar.childCount).Select(i => avatar.GetChild(i).name));
                    Note("avatar children: " + children);
                });
                Flush();
                _running = false;
                yield break;
            }
            Note("animator on \"" + animator.gameObject.name + "\" with " + animator.parameters.Length + " parameters");
            Step("  animator resolved");

            // ---- S2: desktop / VR mode ----
            Step("  S2 VRMode");
            Run("S2/S3", () =>
            {
                var isVr = ABI_RC.Core.Savior.MetaPort.Instance != null && ABI_RC.Core.Savior.MetaPort.Instance.isUsingVr;
                CheckParam(animator, "VRMode", isVr ? 1f : 0f, 0.01f, "S2 VRMode matches the running device");
                CheckParam(animator, "Upright", 1f, 0.2f, "S3 Upright is ~1 while standing");
            });

            // ---- G1/G2/G3: gesture weight semantics (inject CVR gesture values) ----
            Step("  G1..G4 gesture weight (5 gestures)");
            yield return Gesture(avatar, animator, 0f, "neutral",
                weightBarTall: false, weightGate: false, fistGate: false, derivedWeight: 0f);
            yield return Gesture(avatar, animator, 0.8f, "fist 80%",
                weightBarTall: true, weightGate: true, fistGate: true, derivedWeight: 0.8f);
            yield return Gesture(avatar, animator, 0.3f, "fist 30%",
                weightBarTall: false, weightGate: false, fistGate: false, derivedWeight: 0.3f);
            yield return Gesture(avatar, animator, -1f, "open hand",
                weightBarTall: true, weightGate: true, fistGate: false, derivedWeight: 1f);
            yield return Gesture(avatar, animator, 4f, "point",
                weightBarTall: true, weightGate: true, fistGate: false, derivedWeight: 1f);
            _gestureOverride = float.NaN;

            // ---- V1: velocity magnitude consistency (move the player while sampling) ----
            // The input system overwrites movementVector every frame, so drive the player by
            // teleporting it along a path instead; the movement system derives velocity from that.
            Step("  V1 velocity (walking, ~4s)");
            InjectMovement = true;
            MovementOverride = new Vector3(0f, 0f, 1f);
            var maxMagnitude = 0f;
            var maxExpected = 0f;
            var worstError = 0f;
            var previousExpected = 0f;
            // proof that the driver squares and roots rather than copying an axis: at some sample
            // the magnitude must exceed the largest single component by a clear margin
            var bestDiagonalRatio = 0f;
            var bestDiagonalAxis = 0f;
            var bestDiagonalMagnitude = 0f;
            for (var i = 0; i < 240; i++)
            {
                // move diagonally so several axes are nonzero at once, and reverse halfway so the
                // player ends up roughly where it started
                MovementOverride = i < 120 ? new Vector3(1f, 0f, 1f) : new Vector3(-1f, 0f, -1f);
                yield return null;
                Run("V1 sample", () =>
                {
                    var x = ReadParam(animator, "VelocityX");
                    var y = ReadParam(animator, "VelocityY");
                    var z = ReadParam(animator, "VelocityZ");
                    var magnitude = ReadParam(animator, "#VelocityMagnitude");
                    var expected = Mathf.Sqrt(x * x + y * y + z * z);
                    maxMagnitude = Mathf.Max(maxMagnitude, magnitude);
                    maxExpected = Mathf.Max(maxExpected, expected);
                    // the driver writes the parameter during the animator evaluation, so its value
                    // is one frame behind: accept a match against this or the previous frame
                    if (i > 30)
                    {
                        var error = Mathf.Min(Mathf.Abs(magnitude - expected), Mathf.Abs(magnitude - previousExpected));
                        worstError = Mathf.Max(worstError, error);

                        var largestAxis = Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z)));
                        if (largestAxis > 0.3f && magnitude / largestAxis > bestDiagonalRatio)
                        {
                            bestDiagonalRatio = magnitude / largestAxis;
                            bestDiagonalAxis = largestAxis;
                            bestDiagonalMagnitude = magnitude;
                        }
                    }
                    previousExpected = expected;
                });
            }
            InjectMovement = false;
            MovementOverride = Vector3.zero;
            Run("V1", () =>
            {
                Check(worstError < 0.15f, "V1 #VelocityMagnitude tracks sqrt(VelocityX^2+Y^2+Z^2) while moving, allowing one frame of driver latency (worst error " + worstError.ToString("0.000") + ", max magnitude " + maxMagnitude.ToString("0.00") + ", max expected " + maxExpected.ToString("0.00") + ")");
                Check(maxExpected > 0.3f, "V1 the injected motion produced nonzero VelocityX/Y/Z (max " + maxExpected.ToString("0.00") + ")");
                Check(maxMagnitude > 0.3f, "V1 #VelocityMagnitude became nonzero (max " + maxMagnitude.ToString("0.00") + ")");
                // a copy of one axis (or its absolute value) would give a ratio of ~1.0
                Check(bestDiagonalRatio > 1.15f,
                    "V1 #VelocityMagnitude exceeds the largest single axis on diagonal motion, so it really is a sum of squares (magnitude " +
                    bestDiagonalMagnitude.ToString("0.00") + " vs largest axis " + bestDiagonalAxis.ToString("0.00") +
                    ", ratio " + bestDiagonalRatio.ToString("0.00") + ")");
            });
            yield return new WaitForSeconds(1f);

            // ---- S3: upright while crouching (injected) ----
            Step("  S3 crouch");
            var uprightStanding = 0f;
            var characterController = BetterBetterCharacterController.Instance;
            Run("S3 crouch start", () =>
            {
                uprightStanding = ReadParam(animator, "Upright");
                if (characterController != null)
                {
                    characterController.crouching = true;
                }
            });
            yield return new WaitForSeconds(1.5f);
            Run("S3 crouch check", () =>
            {
                if (characterController == null)
                {
                    Note("S3 crouch injection skipped (no character controller)");
                    return;
                }
                var uprightCrouching = ReadParam(animator, "Upright");
                Check(uprightCrouching < uprightStanding - 0.05f,
                    "S3 Upright drops while crouching (standing " + uprightStanding.ToString("0.00") + " -> crouching " + uprightCrouching.ToString("0.00") + ")");
                characterController.crouching = false;
            });
            yield return new WaitForSeconds(1.5f);

            // ---- S1: mute (injected) ----
            Step("  S1 mute");
            var muteWas = false;
            Run("S1 mute on", () =>
            {
                muteWas = Comms_Manager.IsMicMuted;
                Comms_Manager.IsMicMuted = true;
            });
            yield return new WaitForSeconds(1.5f);
            var mutedValue = 0f;
            Run("S1 sample muted", () =>
            {
                mutedValue = ReadParam(animator, "MuteSelf");
                Comms_Manager.IsMicMuted = false;
            });
            yield return new WaitForSeconds(1.5f);
            Run("S1 check", () =>
            {
                var unmutedValue = ReadParam(animator, "MuteSelf");
                Check(mutedValue > 0.5f && unmutedValue < 0.5f,
                    "S1 MuteSelf follows the mic mute state (muted " + mutedValue.ToString("0.0") + ", unmuted " + unmutedValue.ToString("0.0") + ")");
                Comms_Manager.IsMicMuted = muteWas;
            });

            // ---- constraints ----
            Step("  C1..C8 constraints");
            Transform leftHand = null;
            Transform head = null;
            Run("resolve bones", () =>
            {
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
            });
            yield return null;
            if (leftHand == null || head == null)
            {
                Check(false, "constraint checks skipped: humanoid bones unavailable (leftHand=" + (leftHand != null) + ", head=" + (head != null) + ")");
                Flush();
                _running = false;
                yield break;
            }

            Run("C1..C8", () =>
            {
                CheckObject(avatar, "Constraints/ParentC", target => CheckClose(target.position,
                    leftHand.position + leftHand.rotation * new Vector3(0f, 0.15f, 0f),
                    "C1 ParentC hovers at the hand + rotated offset"));
                CheckObject(avatar, "Constraints/PositionC", target => CheckClose(target.position,
                    leftHand.position + new Vector3(0f, 0.25f, 0f),
                    "C2 PositionC follows the hand + world offset"));
                CheckObject(avatar, "Constraints/RotationC", target => Check(
                    Quaternion.Angle(target.rotation, leftHand.rotation) < AngleToleranceDegrees,
                    "C3 RotationC copies the hand rotation (angle " + Quaternion.Angle(target.rotation, leftHand.rotation).ToString("0.0") + " deg)"));
                CheckObject(avatar, "Constraints/ScaleC", target => Check(
                    Mathf.Abs(target.lossyScale.y - 0.05f) < 0.01f,
                    "C4 ScaleC stays at 5cm (lossy " + target.lossyScale.y.ToString("0.000") + ")"));
                CheckObject(avatar, "Constraints/AimC", target =>
                {
                    var angle = Vector3.Angle(target.forward, (leftHand.position - target.position).normalized);
                    Check(angle < AngleToleranceDegrees, "C5 AimC points at the hand (angle " + angle.ToString("0.0") + " deg)");
                });
                CheckObject(avatar, "Constraints/LookAtC", target =>
                {
                    var angle = Vector3.Angle(target.forward, (leftHand.position - target.position).normalized);
                    Check(angle < AngleToleranceDegrees, "C6 LookAtC looks at the hand (angle " + angle.ToString("0.0") + " deg)");
                });
                CheckObject(avatar, "Constraints/RedirTarget", target => CheckClose(target.position,
                    leftHand.position + new Vector3(0f, 0.35f, 0f),
                    "C7 RedirTarget follows (Target Transform redirect)"));
                // merged constraint: sources are weight-averaged, then the (first constraint's)
                // offset is applied once on top
                CheckObject(avatar, "Constraints/MergeC", target => CheckClose(target.position,
                    (leftHand.position + head.position) / 2f + new Vector3(0f, 0.45f, 0f),
                    "C8 MergeC sits at the hand/head midpoint plus the merged offset"));
            });

            // ---- C9: animated constraint via the menu parameter ----
            Step("  C9 animated constraint");
            var animC = avatar.Find("Constraints/AnimC");
            Run("C9 on", () => PlayerSetup.Instance.ChangeAnimatorParam("AnimConstraint", 1f));
            yield return new WaitForSeconds(1f);
            Run("C9 check on", () => CheckObject(avatar, "Constraints/AnimC", target =>
            {
                CheckClose(target.position, leftHand.position + new Vector3(0f, 0.55f, 0f),
                    "C9 AnimC follows while the menu toggle is ON");
                // the rebound animation drives the Unity constraint's own properties
                var constraint = target.GetComponent<PositionConstraint>();
                Check(constraint != null && constraint.constraintActive && constraint.weight > 0.9f,
                    "C9 animated IsActive/GlobalWeight reached the Unity constraint (active " +
                    (constraint != null ? constraint.constraintActive.ToString() : "n/a") + ", weight " +
                    (constraint != null ? constraint.weight.ToString("0.00") : "n/a") + ")");
            }));
            Run("C9 off", () => PlayerSetup.Instance.ChangeAnimatorParam("AnimConstraint", 0f));
            yield return new WaitForSeconds(1f);
            // a deactivated constraint stops solving and leaves the transform where it was
            // (same in VRChat), so verify the animated properties rather than the position
            Run("C9 check off", () => CheckObject(avatar, "Constraints/AnimC", target =>
            {
                var constraint = target.GetComponent<PositionConstraint>();
                Check(constraint != null && !constraint.constraintActive && constraint.weight < 0.1f,
                    "C9 toggling OFF deactivated the Unity constraint (active " +
                    (constraint != null ? constraint.constraintActive.ToString() : "n/a") + ", weight " +
                    (constraint != null ? constraint.weight.ToString("0.00") : "n/a") + ")");
            }));

            // ---- group visibility toggles ----
            Step("  group visibility toggles");
            Run("toggle off", () => PlayerSetup.Instance.ChangeAnimatorParam("ShowGesture", 0f));
            yield return new WaitForSeconds(0.5f);
            Run("toggle off check", () => CheckObject(avatar, "Panel/Gesture", target =>
                Check(!target.gameObject.activeSelf, "Show Gesture OFF hides the gesture group")));
            Run("toggle on", () => PlayerSetup.Instance.ChangeAnimatorParam("ShowGesture", 1f));
            yield return new WaitForSeconds(0.5f);
            Run("toggle on check", () => CheckObject(avatar, "Panel/Gesture", target =>
                Check(target.gameObject.activeSelf, "Show Gesture ON shows the gesture group")));

            Flush();
            _running = false;
        }

        IEnumerator Gesture(Transform avatar, Animator animator, float value, string label, bool weightBarTall, bool weightGate, bool fistGate, float derivedWeight)
        {
            _gestureOverride = value;
            yield return new WaitForSeconds(1f);
            Run("G " + label, () =>
            {
                CheckObject(avatar, "Panel/Gesture/WeightBar", target => Check(
                    (target.localScale.y > 0.2f) == weightBarTall,
                    "G1 WeightBar at " + label + " is " + (weightBarTall ? "tall" : "short") + " (scaleY " + target.localScale.y.ToString("0.000") + ")"));
                CheckObject(avatar, "Panel/Gesture/WeightGate", target => Check(
                    target.gameObject.activeSelf == weightGate,
                    "G2 WeightGate at " + label + " is " + (weightGate ? "ON" : "OFF")));
                CheckObject(avatar, "Panel/Gesture/FistGate", target => Check(
                    target.gameObject.activeSelf == fistGate,
                    "G3 FistGate at " + label + " is " + (fistGate ? "ON" : "OFF")));
                // derived mode only (identified by the feed layer): the generated parameter
                // reproduces the VRChat weight. In fold mode the parameter exists but is unfed.
                if (HasFeedLayer(animator))
                {
                    var weight = ReadParam(animator, "#GestureLeftWeight");
                    Check(Mathf.Abs(weight - derivedWeight) < 0.05f,
                        "G4 #GestureLeftWeight at " + label + " is " + weight.ToString("0.00") + " (expected " + derivedWeight.ToString("0.00") + ")");
                }
            });
        }

        // resolves a child by path and reports a miss instead of throwing
        void CheckObject(Transform avatar, string path, Action<Transform> check)
        {
            var target = avatar.Find(path);
            if (target == null)
            {
                Check(false, "object \"" + path + "\" not found on the avatar");
                return;
            }
            check(target);
        }

        static bool HasParameter(Animator animator, string name)
        {
            return animator.parameters.Any(parameter => parameter.name == name);
        }

        static bool HasFeedLayer(Animator animator)
        {
            for (var i = 0; i < animator.layerCount; i++)
            {
                if (animator.GetLayerName(i).Contains("VRC3CVR_GestureLeftWeight"))
                {
                    return true;
                }
            }
            return false;
        }

        void CheckParam(Animator animator, string name, float expected, float tolerance, string message)
        {
            if (!HasParameter(animator, name))
            {
                _report.Add("FAIL " + message + " (parameter \"" + name + "\" missing)");
                return;
            }
            var value = ReadParam(animator, name);
            Check(Mathf.Abs(value - expected) < tolerance, message + " (value " + value.ToString("0.00") + ")");
        }

        // GetFloat only works on float parameters; bools and ints silently read back as 0
        static float ReadParam(Animator animator, string name)
        {
            var parameter = animator.parameters.FirstOrDefault(p => p.name == name);
            if (parameter == null)
            {
                return float.NaN;
            }
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Bool: return animator.GetBool(name) ? 1f : 0f;
                case AnimatorControllerParameterType.Int: return animator.GetInteger(name);
                default: return animator.GetFloat(name);
            }
        }

        void CheckClose(Vector3 actual, Vector3 expected, string message)
        {
            var distance = Vector3.Distance(actual, expected);
            Check(distance < PositionTolerance, message + " (off by " + distance.ToString("0.000") + "m)");
        }

        void Check(bool condition, string message)
        {
            _report.Add((condition ? "PASS " : "FAIL ") + message);
        }

        void Note(string message)
        {
            _report.Add("INFO " + message);
        }

        // appends so a suite run keeps the results of every avatar even if a later one hangs
        void Flush()
        {
            File.AppendAllLines(ReportPath, _report);
            foreach (var line in _report)
            {
                MelonLogger.Msg(line);
            }
            var failed = _report.Count(line => line.StartsWith("FAIL"));
            MelonLogger.Msg("checks written: " + _report.Count + " lines, " + failed + " FAIL");
            _report.Clear();
        }
    }
}
