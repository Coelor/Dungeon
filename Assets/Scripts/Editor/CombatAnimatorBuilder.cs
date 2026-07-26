using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Dungeon.EditorTools
{
    /// <summary>
    ///     Builds the player animator controller used by <see cref="CombatController" />:
    ///     copies the Synty base locomotion controller and adds a "Combat" layer with the sword
    ///     attack/dodge/block/draw states, plus arm/hand mask layers holding the sword idle pose,
    ///     modeled on the sword pack's BaseLocomotionMasking sample. Safe to re-run: the combat
    ///     layers are rebuilt in place while the base locomotion layers are left untouched.
    /// </summary>
    public static class CombatAnimatorBuilder
    {
        private const string _SOURCE_CONTROLLER = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/AC_Polygon_Masculine.controller";
        private const string _OUTPUT_DIR = "Assets/Animations";
        private const string _OUTPUT_CONTROLLER = _OUTPUT_DIR + "/AC_DungeonPlayer.controller";
        private const string _CLIP_ROOT = "Assets/Synty/AnimationSwordCombat/Animations/Polygon";
        private const string _ARM_MASK_PATH = "Assets/Synty/AnimationSwordCombat/Meshes/Mask_Arm_R.mask";
        private const string _HAND_MASK_PATH = "Assets/Synty/AnimationSwordCombat/Meshes/Mask_Hand_R.mask";
        private const string _PLAYER_PREFAB = "Assets/Prefabs/PF_Player.prefab";

        private static readonly string[] _COMBAT_LAYER_NAMES = { "Combat", "Sword_Arm", "Sword_Hand" };

        private static readonly (string state, string clip)[] _COMBAT_STATES =
        {
            ("Light_A", "A_Attack_LightCombo01A_Sword"),
            ("Light_B", "A_Attack_LightCombo01B_Sword"),
            ("Light_C", "A_Attack_LightCombo01C_Sword"),
            ("Heavy_A", "A_Attack_HeavyCombo01A_Sword"),
            ("Heavy_B", "A_Attack_HeavyCombo01B_Sword"),
            ("Heavy_C", "A_Attack_HeavyCombo01C_Sword"),
            ("Dodge_F", "A_DodgeRoll_F_RootMotion_Sword"),
            ("Dodge_B", "A_DodgeRoll_B_RootMotion_Sword"),
            ("Dodge_L", "A_DodgeRoll_L_RootMotion_Sword"),
            ("Dodge_R", "A_DodgeRoll_R_RootMotion_Sword"),
            ("Block_Begin", "A_Block_Begin_Sword"),
            ("Block_Loop", "A_Block_Loop_Sword"),
            ("Block_End", "A_Block_End_Sword"),
            ("Draw", "A_Draw_Sword_Masc"),
            ("Sheathe", "A_Sheathe_Sword_Masc")
        };

        [MenuItem("Dungeon/Build Combat Animator")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(_OUTPUT_DIR))
            {
                AssetDatabase.CreateFolder("Assets", "Animations");
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(_OUTPUT_CONTROLLER) == null)
            {
                if (!AssetDatabase.CopyAsset(_SOURCE_CONTROLLER, _OUTPUT_CONTROLLER))
                {
                    Debug.LogError($"CombatAnimatorBuilder: could not copy {_SOURCE_CONTROLLER} to {_OUTPUT_CONTROLLER}.");
                    return;
                }
            }

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(_OUTPUT_CONTROLLER);

            RemoveCombatLayers(controller);
            AddCombatLayer(controller);
            AddSwordPoseLayers(controller);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            AssignToPlayerPrefab(controller);

            Debug.Log(
                $"CombatAnimatorBuilder: built {_OUTPUT_CONTROLLER} with layers "
                + string.Join(", ", _COMBAT_LAYER_NAMES)
                + " and assigned it to the player prefab."
            );
        }

        /// <summary>
        ///     Removes previously generated combat layers so the builder can be re-run safely.
        /// </summary>
        private static void RemoveCombatLayers(AnimatorController controller)
        {
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                if (_COMBAT_LAYER_NAMES.Contains(controller.layers[i].name))
                {
                    controller.RemoveLayer(i);
                }
            }
        }

        private static void AddCombatLayer(AnimatorController controller)
        {
            AnimatorStateMachine stateMachine = new AnimatorStateMachine
            {
                name = "Combat",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            AnimatorState empty = stateMachine.AddState("Empty", new Vector3(0f, 0f, 0f));
            stateMachine.defaultState = empty;

            Dictionary<string, AnimatorState> states = new Dictionary<string, AnimatorState>();
            int index = 0;

            foreach ((string stateName, string clipName) in _COMBAT_STATES)
            {
                AnimationClip clip = FindClip(clipName);

                if (clip == null)
                {
                    Debug.LogWarning($"CombatAnimatorBuilder: no clip found for '{clipName}', state '{stateName}' will be empty.");
                }

                Vector3 position = new Vector3(300f, index * 60f, 0f);
                AnimatorState state = stateMachine.AddState(stateName, position);
                state.motion = clip;
                states[stateName] = state;
                index++;
            }

            // Blocking holds in the loop state until the button is released.
            AnimatorStateTransition beginToLoop = states["Block_Begin"].AddTransition(states["Block_Loop"]);
            beginToLoop.hasExitTime = true;
            beginToLoop.exitTime = 0.9f;
            beginToLoop.hasFixedDuration = true;
            beginToLoop.duration = 0.1f;

            AnimatorControllerLayer layer = new AnimatorControllerLayer
            {
                name = "Combat",
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = stateMachine
            };
            controller.AddLayer(layer);
        }

        /// <summary>
        ///     Adds the arm and hand mask layers that hold the sword idle pose during locomotion,
        ///     mirroring the sword pack's BaseLocomotionMasking sample (weights driven at runtime).
        /// </summary>
        private static void AddSwordPoseLayers(AnimatorController controller)
        {
            AnimationClip swordIdle = FindClip("A_Idle_Base_Sword");
            AvatarMask armMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(_ARM_MASK_PATH);
            AvatarMask handMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(_HAND_MASK_PATH);

            AddMaskedIdleLayer(controller, "Sword_Arm", armMask, swordIdle);
            AddMaskedIdleLayer(controller, "Sword_Hand", handMask, swordIdle);
        }

        private static void AddMaskedIdleLayer(AnimatorController controller, string layerName, AvatarMask mask, AnimationClip clip)
        {
            if (mask == null)
            {
                Debug.LogWarning($"CombatAnimatorBuilder: avatar mask for layer '{layerName}' not found; layer skipped.");
                return;
            }

            AnimatorStateMachine stateMachine = new AnimatorStateMachine
            {
                name = layerName,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            AnimatorState idle = stateMachine.AddState("Idle_Sword", new Vector3(0f, 0f, 0f));
            idle.motion = clip;
            stateMachine.defaultState = idle;

            AnimatorControllerLayer layer = new AnimatorControllerLayer
            {
                name = layerName,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                avatarMask = mask,
                stateMachine = stateMachine
            };
            controller.AddLayer(layer);
        }

        /// <summary>
        ///     Finds an animation clip by its FBX file name inside the sword combat pack.
        /// </summary>
        private static AnimationClip FindClip(string clipName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{clipName} t:AnimationClip", new[] { _CLIP_ROOT }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (Path.GetFileNameWithoutExtension(path) != clipName)
                {
                    continue;
                }

                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview"));

                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }

        private static void AssignToPlayerPrefab(AnimatorController controller)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(_PLAYER_PREFAB) == null)
            {
                Debug.LogWarning($"CombatAnimatorBuilder: player prefab not found at {_PLAYER_PREFAB}; assign {_OUTPUT_CONTROLLER} manually.");
                return;
            }

            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(_PLAYER_PREFAB))
            {
                Animator animator = scope.prefabContentsRoot.GetComponentInChildren<Animator>();

                if (animator != null)
                {
                    animator.runtimeAnimatorController = controller;
                }
                else
                {
                    Debug.LogWarning("CombatAnimatorBuilder: no Animator found on the player prefab.");
                }
            }
        }
    }
}
