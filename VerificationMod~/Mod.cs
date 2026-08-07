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

        // The input manager rewrites movementVector and jump every frame, so injected input has to
        // be applied after it runs; a postfix on its update is the only ordering that holds.
        internal static Vector3 MovementOverride = Vector3.zero;
        internal static bool InjectMovement;
        internal static bool InjectJump;

        public override void OnInitializeMelon()
        {
            var updateInput = typeof(CVRInputManager).GetMethod("UpdateInput",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (updateInput != null)
            {
                HarmonyInstance.Patch(updateInput, postfix: new HarmonyMethod(typeof(VerificationMod).GetMethod(
                    nameof(ForceInput), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));
                MelonLogger.Msg("patched CVRInputManager.UpdateInput for input injection");
            }
            else
            {
                MelonLogger.Warning("CVRInputManager.UpdateInput not found; input injection is unavailable");
            }
        }

        static void ForceInput(CVRInputManager __instance)
        {
            if (InjectMovement)
            {
                __instance.movementVector = MovementOverride;
            }
            if (InjectJump)
            {
                __instance.jump = true;
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

            // The probe avatar is ChilloutVR-native and answers client questions rather than
            // conversion ones, so it carries a different readiness marker and a different routine.
            // It is OPTIONAL: a run without it is still a complete conversion check.
            var avatars = new (string label, string id, string marker, bool probe, bool required)[]
            {
                ("Fold", ReadId("fold"), "Constraints/AnimC", false, true),
                ("Derived", ReadId("derived"), "Constraints/AnimC", false, true),
                ("Probe", ReadId("probe"), "Armature/Hips", true, false),
            };
            var unconfigured = avatars.Where(entry => entry.required && string.IsNullOrEmpty(entry.id)).ToArray();
            if (unconfigured.Length > 0)
            {
                var missing = string.Join(", ", unconfigured.Select(entry => entry.label.ToLower()));
                File.AppendAllText(ReportPath, "FAIL no content id for " + missing + " in " +
                    Path.GetFullPath(IdFilePath) + " (write \"fold=<id>\" / \"derived=<id>\", one per line)\n");
                Step("missing avatar ids in " + Path.GetFullPath(IdFilePath));
            }
            foreach (var entry in avatars.Where(entry => !entry.required && string.IsNullOrEmpty(entry.id)))
            {
                File.AppendAllText(ReportPath, "INFO no content id for " + entry.label.ToLower() + " in " +
                    Path.GetFullPath(IdFilePath) + "; skipping it (write \"" + entry.label.ToLower() + "=<id>\" to include it)\n");
            }

            foreach (var entry in avatars.Where(entry => !string.IsNullOrEmpty(entry.id)))
            {
                Step("switching to the " + entry.label + " avatar (" + entry.id + ")");
                AssetManagement.Instance.LoadLocalAvatar(entry.id);
                // the switch is asynchronous and the previously worn avatar also carries a
                // verification rig, so wait for one whose object name carries this content id
                var waited = 0f;
                var lastLogged = 0f;
                while (waited < 60f)
                {
                    var loaded = PlayerSetup.Instance != null ? PlayerSetup.Instance.AvatarObject : null;
                    if (loaded != null && loaded.name.Contains(entry.id) && loaded.transform.Find(entry.marker) != null)
                    {
                        break;
                    }
                    waited += Time.deltaTime;
                    if (waited - lastLogged >= 5f)
                    {
                        lastLogged = waited;
                        var name = loaded != null ? loaded.name : "(none)";
                        Step("  still waiting for " + entry.label + " (" + waited.ToString("0") + "s, currently \"" + name + "\")");
                    }
                    yield return null;
                }
                var avatar = PlayerSetup.Instance != null ? PlayerSetup.Instance.AvatarObject : null;
                if (avatar == null || !avatar.name.Contains(entry.id) || avatar.transform.Find(entry.marker) == null)
                {
                    File.AppendAllText(ReportPath, "FAIL " + entry.label + ": avatar did not load within 60s (worn: \"" + (avatar != null ? avatar.name : "none") + "\")\n");
                    Step(entry.label + ": avatar did not load, skipping");
                    continue;
                }
                // give the avatar a moment to finish initializing after it appears
                yield return new WaitForSeconds(2f);
                Step(entry.label + ": avatar loaded, running checks");
                File.AppendAllText(ReportPath, "INFO ===== " + entry.label + " mode (" + entry.id + ") =====\n");
                yield return entry.probe ? RunProbeChecks(avatar.transform) : RunChecks(avatar.transform);
                Step(entry.label + ": checks finished");
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
                CheckParam(animator, UprightOf(animator), 1f, 0.2f, "S3 Upright is ~1 while standing");
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

            // ---- V2: the CONVERTED blend tree actually reads the avatar-local velocity ----
            // V1 above only proves #VelocityMagnitude (frame-invariant, unaffected by the local
            // fix) tracks the raw axes. This proves the fix's actual output — VelocityBar, driven
            // by the derived #VelocityZLocal — behaves correctly in the one place a magnitude
            // check cannot: at a heading that is NOT axis-aligned. There, walking forward and
            // strafing right put the same component on WORLD VelocityZ, so a conversion that fed
            // the blend tree world-space velocity would raise the bar for both. Only reading the
            // avatar-local value tells them apart. Shape reused from M1 below (heading forced and
            // re-asserted every frame, since the movement system owns rotation otherwise).
            Step("  V2 VelocityBar direction (walking, ~4s)");
            // prefixed distinctly from M1's own playerTransform/originalRotation/heading below: a
            // nested block in C# cannot reuse a name the enclosing method body also declares, even
            // in a non-overlapping later statement (CS0136)
            var v2PlayerTransform = BetterBetterCharacterController.Instance != null
                ? BetterBetterCharacterController.Instance.transform
                : (PlayerSetup.Instance != null ? PlayerSetup.Instance.transform : null);
            var v2VelocityBar = avatar.Find("Panel/State/VelocityBar");
            if (v2PlayerTransform == null || v2VelocityBar == null)
            {
                Run("V2", () => Note("V2 skipped: " + (v2PlayerTransform == null ? "no player transform" : "VelocityBar not found")));
            }
            else
            {
                var v2OriginalRotation = v2PlayerTransform.rotation;
                var v2Heading = Quaternion.Euler(0f, v2PlayerTransform.eulerAngles.y + 40f, 0f);
                var forwardPeak = 0f;
                var strafePeak = 0f;

                InjectMovement = true;
                // forward, out and back so the strafe leg below starts from roughly the same spot
                for (var i = 0; i < 120; i++)
                {
                    MovementOverride = new Vector3(0f, 0f, i < 60 ? 1f : -1f);
                    // re-asserted every frame: the movement system owns the rotation otherwise
                    v2PlayerTransform.rotation = v2Heading;
                    yield return null;
                    if (i > 20 && i < 60)
                    {
                        Run("V2 forward sample", () => forwardPeak = Mathf.Max(forwardPeak, v2VelocityBar.localScale.y));
                    }
                }
                // strafe right, out and back, same heading
                for (var i = 0; i < 120; i++)
                {
                    MovementOverride = new Vector3(i < 60 ? 1f : -1f, 0f, 0f);
                    v2PlayerTransform.rotation = v2Heading;
                    yield return null;
                    if (i > 20 && i < 60)
                    {
                        Run("V2 strafe sample", () => strafePeak = Mathf.Max(strafePeak, v2VelocityBar.localScale.y));
                    }
                }
                InjectMovement = false;
                MovementOverride = Vector3.zero;
                v2PlayerTransform.rotation = v2OriginalRotation;

                Run("V2", () =>
                {
                    // The bar's map is linear: scaleY = 0.02 + (v / 2) * 0.38 for v in ChilloutVR's
                    // documented m/s. Correct local-space forward reading approaches the walk speed
                    // itself (2.0 m/s -> scaleY 0.4, the bar's full height). A regression to
                    // world-space velocity puts sin(40 deg) ~= 0.64 of that speed on Z during the
                    // strafe leg (2.0 * 0.64 = 1.28 m/s -> scaleY ~= 0.26). The old 0.2 threshold sat
                    // at the exact midpoint of the range (needing >= 0.95 m/s forward), close enough
                    // to a still-ramping sample window's plausible sub-peak reading to risk a spurious
                    // failure on this test's first real run. 0.15 (>= 0.68 m/s) keeps real margin
                    // under the ~0.4 a correct forward reading should approach, while staying well
                    // clear of the ~0.26 a world-space regression reads on strafe, so the check that
                    // exists to catch that regression still catches it.
                    Check(forwardPeak > 0.15f,
                        "V2 VelocityBar rises walking forward at a 40 deg heading (peak scaleY " + forwardPeak.ToString("0.000") + ")");
                    Check(strafePeak < 0.15f,
                        "V2 VelocityBar stays low strafing right at the same heading (peak scaleY " + strafePeak.ToString("0.000") +
                        ", forward peak was " + forwardPeak.ToString("0.000") + ")");
                });
            }
            yield return new WaitForSeconds(1f);

            // ---- M1/M3: the space and the unit of VelocityX/Y/Z (issue #28 go/no-go) ----
            // VRChat documents these as "Lateral / Vertical / Forward move speed in m/s", which is
            // avatar-LOCAL space. Converting a VRChat locomotion blend tree onto ChilloutVR's own
            // Velocity* only works if ChilloutVR means the same thing: an animator has no way to
            // rotate a vector, so a world-space reading would make that design impossible.
            //
            // The player is turned to a heading that is deliberately NOT axis-aligned and walked
            // forward, then the reported (VelocityX, VelocityZ) is compared against the player's
            // real motion expressed BOTH ways. Real motion comes from the transform's own position
            // delta, so this needs no client API beyond the transform — and the same samples answer
            // the unit question, since that delta is metres per second by construction.
            Step("  M1 velocity space");
            var playerTransform = BetterBetterCharacterController.Instance != null
                ? BetterBetterCharacterController.Instance.transform
                : (PlayerSetup.Instance != null ? PlayerSetup.Instance.transform : null);
            if (playerTransform == null)
            {
                Run("M1", () => Note("M1 skipped: no player transform"));
            }
            else
            {
                // 40 degrees off whatever the player faces now: axis-aligned headings make the two
                // hypotheses numerically identical, which would read as a pass for either one
                var originalRotation = playerTransform.rotation;
                var heading = Quaternion.Euler(0f, playerTransform.eulerAngles.y + 40f, 0f);
                var previousPosition = playerTransform.position;
                var localErrorSum = 0f;
                var worldErrorSum = 0f;
                var separationSum = 0f;
                var velocitySamples = 0;
                var peakMeasuredSpeed = 0f;
                var peakReportedSpeed = 0f;
                // Raw evidence for the one thing the error sums cannot settle by themselves: WHICH
                // transform the client measures against. "local" above is computed from the
                // controller transform this test rotates, so if the client uses a different one —
                // the avatar root, say — that never turned, the local hypothesis loses for the
                // wrong reason. Logging the actual bearings makes the reference frame readable
                // straight off the report: reported bearing == world bearing means world space,
                // and reported == world minus a body's yaw means local space against that body.
                var rawSamples = new List<string>();
                var rawLogged = 0;
                // M4: the salvage path for a world-space reading. The CCK documents
                // RigidBodyLocalVelocityX/Y/Z as "Rigidbody velocity (local space)", resolved from
                // "the Rigidbody that is on the same or in a parent GameObject relative to the
                // Parameter Stream component" — so if the worn avatar has a Rigidbody above it, a
                // stream on the avatar root hands the animator an avatar-local velocity directly,
                // with no trigonometry layer. Whether one exists, and whether its transform turns
                // with the player, is what decides that. Walking up the hierarchy answers both
                // without touching the uploaded avatar.
                Rigidbody ancestorBody = null;
                var ancestry = new List<string>();
                var ancestorBodies = new List<Rigidbody>();
                for (var t = animator.transform; t != null; t = t.parent)
                {
                    var body = t.GetComponent<Rigidbody>();
                    ancestry.Add(t.name + (body != null ? " [Rigidbody]" : ""));
                    if (body != null)
                    {
                        ancestorBodies.Add(body);
                        if (ancestorBody == null)
                        {
                            ancestorBody = body;
                        }
                    }
                }

                InjectMovement = true;
                // out and back, so the player ends where it started: a one-way walk left the
                // player ~7m downrange and the next avatar's run started against a wall
                for (var i = 0; i < 180; i++)
                {
                    MovementOverride = new Vector3(0f, 0f, i < 90 ? 1f : -1f);
                    // re-asserted every frame: the movement system owns the rotation otherwise
                    playerTransform.rotation = heading;
                    yield return null;
                    var sample = i;
                    Run("M1 sample", () =>
                    {
                        var position = playerTransform.position;
                        var worldVelocity = Time.deltaTime > 0f
                            ? (position - previousPosition) / Time.deltaTime
                            : Vector3.zero;
                        previousPosition = position;
                        // let the player accelerate and the heading settle before believing
                        // anything, and skip the turnaround for the same reason
                        if (sample < 30 || (sample >= 90 && sample < 120))
                        {
                            return;
                        }
                        var groundSpeed = new Vector2(worldVelocity.x, worldVelocity.z).magnitude;
                        if (groundSpeed < 0.5f)
                        {
                            return;
                        }
                        // measured against the CURRENT rotation, so the maths stays right even if
                        // the client refused the injected heading
                        var localVelocity = Quaternion.Inverse(playerTransform.rotation) * worldVelocity;
                        var world = new Vector2(worldVelocity.x, worldVelocity.z);
                        var local = new Vector2(localVelocity.x, localVelocity.z);
                        var reported = new Vector2(ReadParam(animator, "VelocityX"), ReadParam(animator, "VelocityZ"));

                        localErrorSum += (reported - local).magnitude;
                        worldErrorSum += (reported - world).magnitude;
                        separationSum += (world - local).magnitude;
                        velocitySamples++;
                        peakMeasuredSpeed = Mathf.Max(peakMeasuredSpeed, groundSpeed);
                        peakReportedSpeed = Mathf.Max(peakReportedSpeed, reported.magnitude);

                        if (rawLogged < 5 && sample % 25 == 0 && reported.magnitude > 0.5f)
                        {
                            rawLogged++;
                            // bearings measured the Unity way: 0 is +Z, growing clockwise
                            var worldBearing = Mathf.Atan2(world.x, world.y) * Mathf.Rad2Deg;
                            var reportedBearing = Mathf.Atan2(reported.x, reported.y) * Mathf.Rad2Deg;
                            rawSamples.Add("M1 raw f" + sample +
                                ": controller yaw " + playerTransform.eulerAngles.y.ToString("0.0") +
                                ", avatar yaw " + animator.transform.eulerAngles.y.ToString("0.0") +
                                ", world bearing " + worldBearing.ToString("0.0") +
                                ", reported bearing " + reportedBearing.ToString("0.0") +
                                ", world-reported " + Mathf.DeltaAngle(reportedBearing, worldBearing).ToString("0.0") +
                                " deg, |world| " + world.magnitude.ToString("0.00") +
                                ", |reported| " + reported.magnitude.ToString("0.00"));
                            // EVERY Rigidbody in the chain, not just the nearest: if the nearest is
                            // kinematic the stream may still be resolving to a different one, and
                            // "the nearest one reads zero" is not the same claim as "no body in the
                            // hierarchy carries the motion"
                            foreach (var body in ancestorBodies)
                            {
                                var bodyLocal = body.transform.InverseTransformDirection(body.velocity);
                                rawSamples.Add("M4 raw f" + sample + ": rigidbody \"" + body.name +
                                    "\" kinematic " + body.isKinematic +
                                    ", yaw " + body.transform.eulerAngles.y.ToString("0.0") +
                                    ", world velocity (" + body.velocity.x.ToString("0.00") + ", " +
                                    body.velocity.z.ToString("0.00") +
                                    "), LOCAL velocity (" + bodyLocal.x.ToString("0.00") + ", " +
                                    bodyLocal.z.ToString("0.00") + ")");
                            }
                            // M6: the conversion that needs no yaw and no trigonometry. ChilloutVR's
                            // MovementX/Y are NORMALISED MOVEMENT INPUT, which is player-local by
                            // construction, and a magnitude is frame-independent — so
                            // (MovementX, MovementY) * |world velocity| should reconstruct exactly
                            // the avatar-local velocity VRChat's locomotion trees expect. Checked
                            // against the reported world vector rotated by the avatar's own yaw,
                            // which keeps both sides free of position-sampling noise.
                            var inputVector = new Vector2(
                                ReadParam(animator, "MovementX"), ReadParam(animator, "MovementY"));
                            var trueLocal3 = Quaternion.Euler(0f, -animator.transform.eulerAngles.y, 0f)
                                * new Vector3(reported.x, 0f, reported.y);
                            var trueLocal = new Vector2(trueLocal3.x, trueLocal3.z);
                            var reconstructed = inputVector * reported.magnitude;
                            rawSamples.Add("M6 raw f" + sample + ": MovementX/Y (" +
                                inputVector.x.ToString("0.00") + ", " + inputVector.y.ToString("0.00") +
                                "), speed " + reported.magnitude.ToString("0.00") +
                                " -> reconstructed local (" + reconstructed.x.ToString("0.00") + ", " +
                                reconstructed.y.ToString("0.00") +
                                ") vs true local (" + trueLocal.x.ToString("0.00") + ", " +
                                trueLocal.y.ToString("0.00") + ")");
                        }
                    });
                }
                InjectMovement = false;
                MovementOverride = Vector3.zero;
                playerTransform.rotation = originalRotation;
                Run("M1", () =>
                {
                    if (velocitySamples == 0)
                    {
                        Note("M1 inconclusive: the player never reached a measurable speed");
                        return;
                    }
                    var localError = localErrorSum / velocitySamples;
                    var worldError = worldErrorSum / velocitySamples;
                    var separation = separationSum / velocitySamples;
                    foreach (var raw in rawSamples)
                    {
                        Note(raw);
                    }
                    Note("M1 read the raw lines as: world-reported ~0 means WORLD space; " +
                         "world-reported ~= a body's yaw means LOCAL space against that body");
                    Note("M4 hierarchy above the avatar: " + string.Join(" < ", ancestry.ToArray()));
                    Note(ancestorBody != null
                        ? "M4 a Rigidbody exists at or above the avatar (\"" + ancestorBody.name +
                          "\"), so a CVRParameterStream RigidBodyLocalVelocity* on the avatar root would resolve to it. " +
                          "Usable only if its LOCAL velocity above tracks the walk direction while its yaw turns with the player"
                        : "M4 NO Rigidbody at or above the avatar — RigidBodyLocalVelocity* cannot resolve, so a " +
                          "world-to-local conversion would have to be built from the transform yaw instead");
                    Note("M1 samples " + velocitySamples +
                         ", mean |reported - local| " + localError.ToString("0.000") +
                         " m/s, mean |reported - world| " + worldError.ToString("0.000") +
                         " m/s, mean |local - world| " + separation.ToString("0.000") + " m/s");
                    // without separation the two hypotheses predict the same numbers and neither
                    // result would mean anything
                    if (separation < 0.5f)
                    {
                        Note("M1 INCONCLUSIVE: the two hypotheses were only " + separation.ToString("0.000") +
                             " m/s apart — the injected heading did not take, so this run proves nothing");
                        return;
                    }
                    Check(localError < worldError * 0.5f,
                        "M1 ChilloutVR reports VelocityX/Z in AVATAR-LOCAL space, as VRChat does (local error " +
                        localError.ToString("0.000") + " vs world error " + worldError.ToString("0.000") + " m/s)");
                    Note("M3 peak measured ground speed " + peakMeasuredSpeed.ToString("0.00") +
                         " m/s vs peak reported |VelocityXZ| " + peakReportedSpeed.ToString("0.00") +
                         " — equal means ChilloutVR reports metres per second, as VRChat does");
                });
                yield return new WaitForSeconds(1f);
            }

            // ---- S3: upright while crouching (injected) ----
            Step("  S3 crouch");
            var uprightStanding = 0f;
            var characterController = BetterBetterCharacterController.Instance;
            Run("S3 crouch start", () =>
            {
                uprightStanding = ReadParam(animator, UprightOf(animator));
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
                var uprightCrouching = ReadParam(animator, UprightOf(animator));
                Check(uprightCrouching < uprightStanding - 0.05f,
                    "S3 Upright drops while crouching (standing " + uprightStanding.ToString("0.00") + " -> crouching " + uprightCrouching.ToString("0.00") + ")");
                characterController.crouching = false;
            });
            yield return new WaitForSeconds(1.5f);

            // ---- M2: what Upright actually READS for each ChilloutVR stance (issue #28) ----
            // S3 only proves Upright moves. VRChat's stock locomotion switches stance at specific
            // values — 0.68/0.70 between standing and crouching, 0.41/0.43 between crouching and
            // prone — so whatever supplies Upright has to LAND in the band for the stance the
            // client is actually in, or the converted state machine picks the wrong one. A
            // conversion that replaced the locomotion layer derives those values itself; one that
            // kept CVR's takes them from the AvatarUpright stream. This measures whichever is in play.
            Step("  M2 upright values");
            if (characterController == null)
            {
                Run("M2", () => Note("M2 skipped: no character controller"));
            }
            else
            {
                var uprightStand = float.NaN;
                var uprightCrouch = float.NaN;
                var uprightProne = float.NaN;
                var proneInjected = false;
                Run("M2 standing", () => uprightStand = ReadParam(animator, UprightOf(animator)));
                Run("M2 crouch on", () => characterController.crouching = true);
                yield return new WaitForSeconds(1.5f);
                Run("M2 crouch read", () =>
                {
                    uprightCrouch = ReadParam(animator, UprightOf(animator));
                    characterController.crouching = false;
                });
                yield return new WaitForSeconds(1.5f);
                Run("M2 prone on", () => proneInjected = TrySetBool(characterController, "prone", true));
                yield return new WaitForSeconds(1.5f);
                Run("M2 prone read", () =>
                {
                    if (!proneInjected)
                    {
                        return;
                    }
                    uprightProne = ReadParam(animator, UprightOf(animator));
                    TrySetBool(characterController, "prone", false);
                });
                yield return new WaitForSeconds(1.5f);
                Run("M2", () =>
                {
                    Note("M2 Upright reads — standing " + uprightStand.ToString("0.000") +
                         ", crouching " + uprightCrouch.ToString("0.000") +
                         ", prone " + (proneInjected ? uprightProne.ToString("0.000") : "n/a (no writable 'prone' on the controller)"));
                    Note("M2 VRChat stock bands for comparison — standing > 0.70, crouching 0.43..0.68, prone < 0.41");
                    Check(uprightCrouch > 0.43f && uprightCrouch < 0.68f,
                        "M2 a ChilloutVR crouch lands inside VRChat's crouch band, so the stream can drive a converted locomotion layer unremapped (" +
                        uprightCrouch.ToString("0.000") + ")");
                });
            }

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

            // ---- L1/L2/L3: the avatar's own Base locomotion, in place of CVR's own layer ----
            // Which clip is actually playing is the only thing that says the replacement survived
            // into the running game.
            Step("  L1..L3 replaced locomotion");
            var locomotionLayer = LayerIndex(animator, "Locomotion/Emotes");
            if (locomotionLayer < 0)
            {
                Check(false, "L no \"Locomotion/Emotes\" layer on the converted animator (layers: " +
                    string.Join(", ", Enumerable.Range(0, animator.layerCount).Select(animator.GetLayerName).ToArray()) +
                    ") — was the avatar converted with the locomotion layer enabled?");
            }
            else
            {
                Run("L1", () =>
                {
                    var idleClip = DominantClip(animator, locomotionLayer, out var idleWeight);
                    Check(idleClip == "Base_CustomIdle",
                        "L1 standing still plays the avatar's own idle clip (\"" + idleClip +
                        "\" at weight " + idleWeight.ToString("0.00") + ")");
                });

                var walkClip = "(not sampled)";
                var walkWeight = 0f;
                InjectMovement = true;
                // out and back, so the player ends up roughly where it started
                for (var i = 0; i < 120; i++)
                {
                    MovementOverride = new Vector3(0f, 0f, i < 60 ? 1f : -1f);
                    yield return null;
                    var sample = i;
                    Run("L2 sample", () =>
                    {
                        // past the acceleration ramp, where the idle child still holds the blend
                        if (sample < 30 || sample >= 60)
                        {
                            return;
                        }
                        var clip = DominantClip(animator, locomotionLayer, out var weight);
                        if (weight >= walkWeight)
                        {
                            walkWeight = weight;
                            walkClip = clip;
                        }
                    });
                }
                InjectMovement = false;
                MovementOverride = Vector3.zero;
                Run("L2", () => Check(walkClip == "LocWalkingForward",
                    "L2 walking forward plays ChilloutVR's own walk clip, so the placeholder was substituted and the " +
                    "velocity conversion drives the blend tree (\"" + walkClip + "\" at weight " + walkWeight.ToString("0.00") + ")"));
                yield return new WaitForSeconds(1f);

                var flightWasAllowed = TryGetBool(characterController, "FlightAllowedInWorld");
                var flightInjected = false;
                Run("L3 flight on", () => flightInjected = TryChangeFlight(characterController, true));
                yield return new WaitForSeconds(1.5f);
                if (!flightInjected)
                {
                    Run("L3", () => Note("L3 not measured: no usable flight control on the character controller"));
                }
                else
                {
                    Run("L3 flying", () => Check(
                        animator.GetCurrentAnimatorStateInfo(locomotionLayer).shortNameHash == Animator.StringToHash("LocFlying"),
                        "L3 flying enters the LocFlying state salvaged out of the replaced layer"));
                    Run("L3 flight off", () => TryChangeFlight(characterController, false));
                    yield return new WaitForSeconds(1.5f);
                    Run("L3 landed", () => Check(
                        animator.GetCurrentAnimatorStateInfo(locomotionLayer).shortNameHash == Animator.StringToHash("Locomotion"),
                        "L3 leaving flight returns to the avatar's own locomotion state"));
                }
                if (flightWasAllowed.HasValue)
                {
                    Run("L3 restore", () => TrySetBool(characterController, "FlightAllowedInWorld", flightWasAllowed.Value));
                }
            }

            yield return StanceHeights(avatar, animator);
            yield return LandingProfile(avatar, animator);

            Flush();
            _running = false;
        }

        // The ChilloutVR-NATIVE probe avatar (built by VRC3CVRCvrProbeAvatar, uploaded as-is).
        // Nothing here passes through the conversion, so every reading is a statement about what
        // the client hands an animator — which is the only thing these questions are about.
        IEnumerator RunProbeChecks(Transform avatar)
        {
            _running = true;
            _report.Clear();
            Note("probe started for " + avatar.name);
            Step("  probe settling (3s)");
            yield return new WaitForSeconds(3f);

            Animator animator = null;
            for (var attempt = 0; attempt < 20 && animator == null; attempt++)
            {
                Run("probe resolve animator", () => animator = avatar.GetComponentInChildren<Animator>(true));
                if (animator == null)
                {
                    yield return new WaitForSeconds(0.5f);
                }
            }
            if (animator == null)
            {
                Check(false, "probe: no Animator found on the avatar");
                Flush();
                _running = false;
                yield break;
            }
            Run("probe params", () => Note("probe animator parameters: " + string.Join(", ",
                animator.parameters.Select(p => p.name + ":" + p.type).ToArray())));

            var playerTransform = BetterBetterCharacterController.Instance != null
                ? BetterBetterCharacterController.Instance.transform
                : avatar;
            var probeOriginalRotation = playerTransform.rotation;

            // ---- P1: the range and reference of TransformGlobalRotationY ----
            // The CCK documents the value range of the Transform rotation sources as unknown, so
            // it is read at two known headings: one reading cannot tell 0..360 from -180..180, and
            // an axis-aligned heading cannot tell a live source from a dead one.
            Step("  P1 world yaw source");
            foreach (var heading in new[] { 200f, 40f })
            {
                var wanted = heading;
                Run("P1 turn", () => playerTransform.rotation = Quaternion.Euler(0f, wanted, 0f));
                yield return new WaitForSeconds(1.5f);
                Run("P1 read", () => Note("P1 at heading " + wanted.ToString("0") +
                    ": TransformGlobalRotationY reads " + ReadParam(animator, "PrbWorldYaw").ToString("0.00") +
                    ", controller yaw " + playerTransform.eulerAngles.y.ToString("0.0") +
                    ", avatar root yaw " + animator.transform.eulerAngles.y.ToString("0.0")));
            }
            Run("P1 upright", () => Note("P1 AvatarUpright source reads " +
                ReadParam(animator, "PrbUpright").ToString("0.000") + " while standing"));

            // ---- P2: does the reconstruction hold, and is the Rigidbody route alive? ----
            // Walked at a heading that is not axis-aligned, out and back so the player ends where
            // it started. Ground truth is the reported WORLD velocity rotated by the avatar's own
            // yaw: both sides then come from the client, with no position-sampling noise.
            Step("  P2 reconstruction (walking, ~3s)");
            var reconErrorSum = 0f;
            var rbLocalErrorSum = 0f;
            var inputMatchSum = 0f;
            var probeSamples = 0;
            var rbAnyMotion = 0f;
            var probeRaw = new List<string>();
            var probeLogged = 0;

            InjectMovement = true;
            for (var i = 0; i < 180; i++)
            {
                MovementOverride = new Vector3(0f, 0f, i < 90 ? 1f : -1f);
                playerTransform.rotation = Quaternion.Euler(0f, 40f, 0f);
                yield return null;
                var sample = i;
                Run("P2 sample", () =>
                {
                    if (sample < 30 || (sample >= 90 && sample < 120))
                    {
                        return;
                    }
                    var world = new Vector2(ReadParam(animator, "VelocityX"), ReadParam(animator, "VelocityZ"));
                    if (world.magnitude < 0.5f)
                    {
                        return;
                    }
                    var trueLocal3 = Quaternion.Euler(0f, -animator.transform.eulerAngles.y, 0f)
                        * new Vector3(world.x, 0f, world.y);
                    var trueLocal = new Vector2(trueLocal3.x, trueLocal3.z);
                    var recon = new Vector2(ReadParam(animator, "PrbReconX"), ReadParam(animator, "PrbReconZ"));
                    var rbLocal = new Vector2(ReadParam(animator, "PrbRbLocalVelX"), ReadParam(animator, "PrbRbLocalVelZ"));
                    var core = new Vector2(ReadParam(animator, "MovementX"), ReadParam(animator, "MovementY"));
                    var input = new Vector2(ReadParam(animator, "PrbInputMoveX"), ReadParam(animator, "PrbInputMoveY"));

                    reconErrorSum += (recon - trueLocal).magnitude;
                    rbLocalErrorSum += (rbLocal - trueLocal).magnitude;
                    inputMatchSum += (input - core).magnitude;
                    // every Rigidbody readout, not just the local pair: "the local variant is zero"
                    // is a weaker claim than "the body carries no motion at all"
                    var rbWorld = new Vector2(ReadParam(animator, "PrbRbVelX"), ReadParam(animator, "PrbRbVelZ"));
                    rbAnyMotion = Mathf.Max(rbAnyMotion, Mathf.Max(
                        Mathf.Max(rbLocal.magnitude, rbWorld.magnitude),
                        ReadParam(animator, "PrbRbSpeed")));
                    probeSamples++;

                    if (probeLogged < 4 && sample % 25 == 0)
                    {
                        probeLogged++;
                        probeRaw.Add("P2 raw f" + sample +
                            ": world (" + world.x.ToString("0.00") + ", " + world.y.ToString("0.00") +
                            "), true local (" + trueLocal.x.ToString("0.00") + ", " + trueLocal.y.ToString("0.00") +
                            "), recon (" + recon.x.ToString("0.00") + ", " + recon.y.ToString("0.00") +
                            "), rb local (" + rbLocal.x.ToString("0.00") + ", " + rbLocal.y.ToString("0.00") +
                            "), rb world (" + ReadParam(animator, "PrbRbVelX").ToString("0.00") + ", " +
                            ReadParam(animator, "PrbRbVelZ").ToString("0.00") +
                            "), rb speed " + ReadParam(animator, "PrbRbSpeed").ToString("0.00") +
                            ", MovementX/Y (" + core.x.ToString("0.00") + ", " + core.y.ToString("0.00") +
                            "), InputMovement (" + input.x.ToString("0.00") + ", " + input.y.ToString("0.00") +
                            "), derived speed " + ReadParam(animator, "PrbSpeed").ToString("0.00") +
                            ", movement ring " + ReadParam(animator, "PrbMoveMag").ToString("0.00"));
                    }
                });
            }
            InjectMovement = false;
            MovementOverride = Vector3.zero;

            // ---- P3: the axis and sign convention, which a forward-only walk cannot show ----
            Step("  P3 strafe convention");
            var strafeLine = "P3 not sampled";
            InjectMovement = true;
            for (var i = 0; i < 60; i++)
            {
                MovementOverride = new Vector3(1f, 0f, 0f);
                playerTransform.rotation = Quaternion.Euler(0f, 40f, 0f);
                yield return null;
                var sample = i;
                Run("P3 sample", () =>
                {
                    if (sample != 45)
                    {
                        return;
                    }
                    var world = new Vector2(ReadParam(animator, "VelocityX"), ReadParam(animator, "VelocityZ"));
                    var trueLocal3 = Quaternion.Euler(0f, -animator.transform.eulerAngles.y, 0f)
                        * new Vector3(world.x, 0f, world.y);
                    strafeLine = "P3 strafing right: true local (" + trueLocal3.x.ToString("0.00") + ", " +
                        trueLocal3.z.ToString("0.00") + "), MovementX/Y (" +
                        ReadParam(animator, "MovementX").ToString("0.00") + ", " +
                        ReadParam(animator, "MovementY").ToString("0.00") + "), recon (" +
                        ReadParam(animator, "PrbReconX").ToString("0.00") + ", " +
                        ReadParam(animator, "PrbReconZ").ToString("0.00") + ")";
                });
            }
            InjectMovement = false;
            MovementOverride = Vector3.zero;
            playerTransform.rotation = probeOriginalRotation;

            Run("P2", () =>
            {
                foreach (var raw in probeRaw)
                {
                    Note(raw);
                }
                Note(strafeLine);
                if (probeSamples == 0)
                {
                    Note("P2 inconclusive: the player never reached a measurable speed");
                    return;
                }
                var reconError = reconErrorSum / probeSamples;
                var rbLocalError = rbLocalErrorSum / probeSamples;
                var inputMatch = inputMatchSum / probeSamples;
                Note("P2 samples " + probeSamples +
                     ", mean |recon - true local| " + reconError.ToString("0.000") +
                     " m/s, mean |rb local - true local| " + rbLocalError.ToString("0.000") +
                     " m/s, mean |InputMovement - MovementX/Y| " + inputMatch.ToString("0.000"));
                Check(reconError < 0.25f,
                    "P2 (MovementX, MovementY) x ground speed reconstructs the avatar-local velocity VRChat's " +
                    "locomotion trees expect, with no yaw and no trigonometry (mean error " +
                    reconError.ToString("0.000") + " m/s)");
                Note(rbAnyMotion > 0.25f
                    ? "P2 the Rigidbody sources DO carry motion (peak " + rbAnyMotion.ToString("0.00") +
                      "); mean local error " + rbLocalError.ToString("0.000") + " m/s says whether they are avatar-local"
                    : "P2 the Rigidbody sources report no motion at all (peak " + rbAnyMotion.ToString("0.00") +
                      ") — RigidBodyLocalVelocity* cannot drive a converted locomotion layer");
            });

            yield return StanceHeights(avatar, animator);
            yield return LandingProfile(avatar, animator);

            Flush();
            _running = false;
        }

        // H: how far the body actually drops when crouching. Driving the same controllers in the
        // editor lowers the hips by 0.43m (crouch) and 0.57m (prone), so measuring the bone here
        // says whether the client keeps that or discards it. Runs on the converted avatars and on
        // the native probe alike, which is what makes the three readings comparable.
        IEnumerator StanceHeights(Transform avatar, Animator animator)
        {
            var characterController = BetterBetterCharacterController.Instance;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (hips == null)
            {
                Run("H", () => Note("H not measured: the avatar has no hips bone"));
                yield break;
            }

            var standHips = float.NaN;
            void Read(string label)
            {
                var hipsY = hips.position.y;
                Note("H " + label +
                     ": hips=" + hipsY.ToString("F4") +
                     " root=" + avatar.position.y.ToString("F4") +
                     " head=" + (head != null ? head.position.y.ToString("F4") : "n/a") +
                     " Upright=" + ReadParam(animator, UprightOf(animator)).ToString("F3") +
                     " state=" + StateNameOf(animator, 0) +
                     (float.IsNaN(standHips) ? "" : " dHips=" + (hipsY - standHips).ToString("F4")));
                if (float.IsNaN(standHips)) standHips = hipsY;
            }

            Step("  H stance heights");
            yield return new WaitForSeconds(1.5f);
            Run("H stand", () => Read("stand "));

            var crouched = false;
            Run("H crouch on", () =>
            {
                if (characterController == null) return;
                characterController.crouching = true;
                crouched = true;
            });
            yield return new WaitForSeconds(1.5f);
            Run("H crouch", () =>
            {
                if (!crouched)
                {
                    Note("H crouch not measured: no character controller");
                    return;
                }
                Read("crouch");
                characterController.crouching = false;
            });
            yield return new WaitForSeconds(1.5f);

            var proned = false;
            Run("H prone on", () => proned = TrySetBool(characterController, "prone", true));
            yield return new WaitForSeconds(1.5f);
            Run("H prone", () =>
            {
                if (!proned)
                {
                    Note("H prone not measured: no writable 'prone' on the controller");
                    return;
                }
                Read("prone ");
                TrySetBool(characterController, "prone", false);
            });
            yield return new WaitForSeconds(1.5f);

            Run("H body weights", () => Note("H " + DescribeBodySystemWeights()));
        }

        // J: the path the body takes through a landing. Pure measurement — there is no threshold to
        // judge against yet, only a native reading to compare a converted one with, which is why the
        // probe runs it too. Sampled per frame: the interesting part is the first few frames after
        // touchdown, which a coarser series would average away.
        IEnumerator LandingProfile(Transform avatar, Animator animator)
        {
            var characterController = BetterBetterCharacterController.Instance;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null || characterController == null)
            {
                Run("J", () => Note("J not measured: " +
                    (hips == null ? "the avatar has no hips bone" : "no character controller")));
                yield break;
            }
            // the client's own ground state, if the animator carries it; a converted avatar that
            // dropped the parameter would otherwise report a landing that never comes
            var hasGroundedParam = HasParameter(animator, "Grounded");
            bool Grounded() => hasGroundedParam ? animator.GetBool("Grounded") : characterController.IsGrounded();

            Step("  J landing profile (3 jumps)");
            Note("J series sample format t:hipsY:rootY:state:normalizedTime:grounded, samples separated by ';'" +
                 (hasGroundedParam ? "" : " (ground state read off the character controller: no \"Grounded\" parameter)"));

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                Run("J" + attempt + " stand", () =>
                {
                    characterController.crouching = false;
                    TrySetBool(characterController, "prone", false);
                });
                var settling = 0f;
                while (settling < 3f && !Grounded())
                {
                    settling += Time.deltaTime;
                    yield return null;
                }
                yield return new WaitForSeconds(1f);

                var times = new List<float>();
                var hipsHeights = new List<float>();
                var series = new List<string>();
                var elapsed = 0f;
                var leftGround = false;
                var landedAt = float.NaN;
                var frames = 0;

                InjectJump = true;
                // released after a few frames: held down, the button is a second press the client
                // reads as the double-jump that turns into flight
                while (elapsed < 6f)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                    if (++frames >= 3)
                    {
                        InjectJump = false;
                    }
                    var now = elapsed;
                    Run("J" + attempt + " sample", () =>
                    {
                        var grounded = Grounded();
                        leftGround |= !grounded;
                        if (leftGround && grounded && float.IsNaN(landedAt))
                        {
                            landedAt = now;
                        }
                        var hipsY = hips.position.y;
                        times.Add(now);
                        hipsHeights.Add(hipsY);
                        var info = animator.GetCurrentAnimatorStateInfo(0);
                        var state = StateName(info.shortNameHash);
                        if (animator.IsInTransition(0))
                        {
                            state += ">" + StateName(animator.GetNextAnimatorStateInfo(0).shortNameHash);
                        }
                        series.Add(now.ToString("0.000") + ":" + hipsY.ToString("F4") + ":" +
                            avatar.position.y.ToString("F4") + ":" + state + ":" +
                            info.normalizedTime.ToString("0.000") + ":" + (grounded ? "1" : "0"));
                    });
                    if (!float.IsNaN(landedAt) && elapsed >= landedAt + 2f)
                    {
                        break;
                    }
                }
                InjectJump = false;

                Run("J" + attempt, () =>
                {
                    if (float.IsNaN(landedAt))
                    {
                        Note("J " + attempt + " no landing: the player " +
                             (leftGround ? "left the ground but never came back within 6s" : "never left the ground"));
                        return;
                    }
                    // the resting height AFTER the landing, not the airborne one: the sink is
                    // measured against where the body ends up, which is what an eye compares against
                    var steady = hipsHeights[hipsHeights.Count - 1];
                    var lowest = float.MaxValue;
                    var fastestDown = 0f;
                    var fastestUp = 0f;
                    var holdingFrom = float.NaN;
                    var settled = float.NaN;
                    for (var i = 0; i < times.Count; i++)
                    {
                        if (times[i] < landedAt)
                        {
                            continue;
                        }
                        lowest = Mathf.Min(lowest, hipsHeights[i]);
                        if (i > 0 && times[i] > times[i - 1])
                        {
                            var speed = (hipsHeights[i] - hipsHeights[i - 1]) / (times[i] - times[i - 1]);
                            fastestDown = Mathf.Min(fastestDown, speed);
                            fastestUp = Mathf.Max(fastestUp, speed);
                        }
                        if (Mathf.Abs(hipsHeights[i] - steady) < 0.01f)
                        {
                            if (float.IsNaN(holdingFrom))
                            {
                                holdingFrom = times[i];
                            }
                            else if (float.IsNaN(settled) && times[i] - holdingFrom >= 0.2f)
                            {
                                settled = holdingFrom - landedAt;
                            }
                        }
                        else
                        {
                            holdingFrom = float.NaN;
                        }
                    }
                    Note("J " + attempt + " summary land=" + landedAt.ToString("0.000") +
                         "s hipsMin=" + lowest.ToString("F4") +
                         " steady=" + steady.ToString("F4") +
                         " sink=" + (steady - lowest).ToString("F4") +
                         " vDownMax=" + fastestDown.ToString("F3") +
                         " vUpMax=" + fastestUp.ToString("F3") +
                         " settle=" + (float.IsNaN(settled) ? "n/a" : settled.ToString("0.000") + "s") +
                         " samples=" + times.Count);

                    // full rate through the half second after touchdown, where the spike under
                    // investigation lives; thinned elsewhere so the line stays one line
                    var emitted = new List<string>();
                    var lastEmitted = float.NegativeInfinity;
                    for (var i = 0; i < times.Count; i++)
                    {
                        if ((times[i] < landedAt || times[i] > landedAt + 0.5f) && times[i] - lastEmitted < 0.1f)
                        {
                            continue;
                        }
                        lastEmitted = times[i];
                        emitted.Add(series[i]);
                    }
                    Note("J " + attempt + " series " + string.Join(";", emitted));
                });
            }
        }

        static string StateNameOf(Animator animator, int layer)
        {
            return StateName(animator.GetCurrentAnimatorStateInfo(layer).shortNameHash);
        }

        // Only the hash survives into the running game, so a name is recovered by hashing the ones
        // worth recognising: VRChat's stock Base layer states and ChilloutVR's own locomotion layer.
        static string StateName(int hash)
        {
            foreach (var name in new[]
            {
                "Standing", "Standing_underwear", "Crouching", "Prone", "Locomotion", "LocFlying",
                "Swimming", "Idle", "Crouch", "Stand", "RestoreTracking",
                "JumpStart", "JumpAir", "JumpLand", "Sitting",
                "SmallHop", "Fall", "QuickLand", "HardLand", "Short Fall", "Long Fall", "RestoreToHop",
            })
            {
                if (Animator.StringToHash(name) == hash) return name;
            }
            return "#" + hash;
        }

        // The IK system's own per-part weights, if this client version exposes them as statics.
        static string DescribeBodySystemWeights()
        {
            var type = System.Type.GetType("ABI_RC.Systems.IK.BodySystem, Assembly-CSharp");
            if (type == null)
            {
                return "BodySystem not found; per-part weights not read";
            }
            var values = type.GetFields(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Where(f => f.Name.IndexOf("weight", StringComparison.OrdinalIgnoreCase) >= 0
                            && (f.FieldType == typeof(float) || f.FieldType == typeof(bool)))
                .Select(f => f.Name + "=" + f.GetValue(null))
                .ToArray();
            return values.Length == 0
                ? "BodySystem exposes no static weight fields"
                : "BodySystem " + string.Join(", ", values);
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

        // A conversion that replaced the locomotion layer derives Upright inside the animator, which
        // makes it local (#-prefixed); one that kept CVR's still has the client feed the synced one
        // under its plain name.
        static string UprightOf(Animator animator)
        {
            return HasParameter(animator, "Upright") ? "Upright" : "#Upright";
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

        static int LayerIndex(Animator animator, string name)
        {
            for (var i = 0; i < animator.layerCount; i++)
            {
                if (animator.GetLayerName(i) == name)
                {
                    return i;
                }
            }
            return -1;
        }

        // The clip holding most of the layer's blend, which for a locomotion blend tree is the one
        // the wearer is actually seen doing.
        static string DominantClip(Animator animator, int layer, out float weight)
        {
            var name = "(none)";
            weight = 0f;
            foreach (var info in animator.GetCurrentAnimatorClipInfo(layer))
            {
                if (info.clip != null && info.weight > weight)
                {
                    weight = info.weight;
                    name = info.clip.name;
                }
            }
            return name;
        }

        // ChilloutVR refuses flight in worlds that disallow it, so the permission is granted first.
        // Reflected for the same reason as TrySetBool below.
        static bool TryChangeFlight(object controller, bool flying)
        {
            if (controller == null)
            {
                return false;
            }
            TrySetBool(controller, "FlightAllowedInWorld", true);
            System.Reflection.MethodInfo method;
            try
            {
                method = controller.GetType().GetMethod("ChangeFlight", MemberFlags);
            }
            catch (System.Reflection.AmbiguousMatchException)
            {
                // an overload added by a later client version: no way to tell which one means this
                return false;
            }
            if (method == null)
            {
                return false;
            }
            var parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters.Any(parameter => parameter.ParameterType != typeof(bool)))
            {
                return false;
            }
            var arguments = new object[parameters.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                // ChangeFlight(isFlying, forceUpdate): apply it now rather than on the next input
                arguments[i] = i == 0 ? (object)flying : true;
            }
            method.Invoke(controller, arguments);
            return true;
        }

        const System.Reflection.BindingFlags MemberFlags = System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        // Writes a bool member whose existence is not guaranteed across client versions, so a
        // renamed or absent field degrades to "not measured" instead of failing the build.
        static bool TrySetBool(object target, string name, bool value)
        {
            if (target == null)
            {
                return false;
            }
            var type = target.GetType();
            var field = type.GetField(name, MemberFlags);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(target, value);
                return true;
            }
            var property = type.GetProperty(name, MemberFlags);
            if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
            {
                property.SetValue(target, value, null);
                return true;
            }
            return false;
        }

        // Reads back what TrySetBool would write, so an injected setting can be put back afterwards.
        static bool? TryGetBool(object target, string name)
        {
            if (target == null)
            {
                return null;
            }
            var type = target.GetType();
            var field = type.GetField(name, MemberFlags);
            if (field != null && field.FieldType == typeof(bool))
            {
                return (bool)field.GetValue(target);
            }
            var property = type.GetProperty(name, MemberFlags);
            if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
            {
                return (bool)property.GetValue(target, null);
            }
            return null;
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
