using Dungeon.InputSystem;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    ///     Drives sword combat on top of the base locomotion: tap/hold attacks with combo chains,
    ///     directional dodge rolls (root motion), hold-to-block, and draw/sheathe. Plays states on
    ///     the "Combat" animator layer built by Dungeon > Build Combat Animator, and fades the
    ///     sword-pose mask layers while the sword is drawn.
    /// </summary>
    public class CombatController : MonoBehaviour
    {
        private enum CombatAction
        {
            None,
            Draw,
            Sheathe,
            LightAttack,
            HeavyAttack,
            Dodge,
            Block
        }

        private const string _COMBAT_LAYER = "Combat";
        private const string _SWORD_ARM_LAYER = "Sword_Arm";
        private const string _SWORD_HAND_LAYER = "Sword_Hand";

        private static readonly string[] _LIGHT_STATES = { "Light_A", "Light_B", "Light_C" };
        private static readonly string[] _HEAVY_STATES = { "Heavy_A", "Heavy_B", "Heavy_C" };

        [Header("External Components")]
        [Tooltip("Animator component for controlling player animations")]
        [SerializeField]
        private Animator _animator;
        [Tooltip("InputReader handles player input")]
        [SerializeField]
        private InputReader _inputReader;
        [Tooltip("Character Controller used to apply dodge root motion")]
        [SerializeField]
        private CharacterController _controller;
        [Tooltip("Player animation controller; movement is locked while combat actions play")]
        [SerializeField]
        private PlayerAnimationController _playerAnimationController;

        [Header("Attacks")]
        [Tooltip("Hold the attack button at least this long to perform a heavy attack instead of a light one")]
        [SerializeField]
        private float _heavyHoldThreshold = 0.35f;
        [Tooltip("Earliest normalized time of the current attack at which the next combo input is accepted")]
        [SerializeField]
        [Range(0f, 1f)]
        private float _comboQueueOpenPoint = 0.2f;
        [Tooltip("Normalized time of the current attack at which a queued combo attack starts")]
        [SerializeField]
        [Range(0f, 1f)]
        private float _comboChainPoint = 0.6f;
        [Tooltip("Cross fade duration used when starting combat states")]
        [SerializeField]
        private float _crossFadeDuration = 0.1f;

        [Header("Sword")]
        [Tooltip("Whether the sword starts drawn")]
        [SerializeField]
        private bool _startsDrawn = true;
        [Tooltip("Optional sword object that is re-parented between the hand and back sockets")]
        [SerializeField]
        private GameObject _sword;
        [Tooltip("Socket the sword sits in while drawn (right hand bone)")]
        [SerializeField]
        private Transform _handSocket;
        [Tooltip("Socket the sword sits in while sheathed (spine/hips bone)")]
        [SerializeField]
        private Transform _backSocket;
        [Tooltip("Weight of the arm mask layer while the sword is drawn")]
        [SerializeField]
        [Range(0f, 1f)]
        private float _armLayerWeight = 0.7f;
        [Tooltip("How fast animator layer weights fade in and out")]
        [SerializeField]
        private float _layerWeightFadeSpeed = 10f;

        private CombatAction _currentAction = CombatAction.None;
        private CombatAction _pendingAction = CombatAction.None;
        private bool _isDrawn;
        private bool _attackDecisionPending;
        private float _attackPressTime;
        private int _comboIndex;
        private bool _comboQueued;
        private bool _blockEnding;
        private bool _socketSwapped;
        private string _currentStateName;

        private int _combatLayer = -1;
        private int _swordArmLayer = -1;
        private int _swordHandLayer = -1;

        /// <summary>
        ///     Whether a combat action other than blocking is currently playing.
        /// </summary>
        public bool IsInAction => _currentAction != CombatAction.None && _currentAction != CombatAction.Block;

        /// <summary>
        ///     Whether the player is currently blocking.
        /// </summary>
        public bool IsBlocking => _currentAction == CombatAction.Block && !_blockEnding;

        /// <summary>
        ///     Whether the sword is currently drawn.
        /// </summary>
        public bool IsDrawn => _isDrawn;

        /// <inheritdoc cref="Start" />
        private void Start()
        {
            _combatLayer = _animator.GetLayerIndex(_COMBAT_LAYER);
            _swordArmLayer = _animator.GetLayerIndex(_SWORD_ARM_LAYER);
            _swordHandLayer = _animator.GetLayerIndex(_SWORD_HAND_LAYER);

            if (_combatLayer < 0)
            {
                Debug.LogError(
                    "CombatController: the animator has no 'Combat' layer. Run 'Dungeon > Build Combat Animator' and assign the generated controller."
                );
                enabled = false;
                return;
            }

            _inputReader.onAttackPressed += OnAttackPressed;
            _inputReader.onAttackReleased += OnAttackReleased;
            _inputReader.onDodgePerformed += OnDodge;
            _inputReader.onBlockActivated += OnBlockActivated;
            _inputReader.onBlockDeactivated += OnBlockDeactivated;
            _inputReader.onSheatheToggled += OnSheatheToggled;

            _isDrawn = _startsDrawn;
            PlaceSwordInSocket(_isDrawn ? _handSocket : _backSocket);
            SetSwordLayerWeights(_isDrawn ? 1f : 0f, true);
        }

        /// <inheritdoc cref="OnDestroy" />
        private void OnDestroy()
        {
            if (_inputReader != null)
            {
                _inputReader.onAttackPressed -= OnAttackPressed;
                _inputReader.onAttackReleased -= OnAttackReleased;
                _inputReader.onDodgePerformed -= OnDodge;
                _inputReader.onBlockActivated -= OnBlockActivated;
                _inputReader.onBlockDeactivated -= OnBlockDeactivated;
                _inputReader.onSheatheToggled -= OnSheatheToggled;
            }
        }

        /// <inheritdoc cref="Update" />
        private void Update()
        {
            // A held attack button becomes a heavy attack once the hold threshold passes.
            if (_attackDecisionPending && Time.time - _attackPressTime >= _heavyHoldThreshold)
            {
                _attackDecisionPending = false;
                TryStartAttack(true);
            }

            UpdateCurrentAction();
            UpdateLayerWeights();
        }

        /// <summary>
        ///     Applies dodge root motion through the character controller. Locomotion clips are
        ///     authored in place, so all other states apply no motion here.
        /// </summary>
        private void OnAnimatorMove()
        {
            if (_currentAction != CombatAction.Dodge)
            {
                return;
            }

            Vector3 delta = _animator.deltaPosition;
            delta.y = 0f;
            _controller.Move(delta);
            transform.rotation *= _animator.deltaRotation;
        }

        #region Input Handlers

        private void OnAttackPressed()
        {
            if (_currentAction == CombatAction.LightAttack || _currentAction == CombatAction.HeavyAttack)
            {
                TryQueueCombo();
                return;
            }

            if (!CanStartAction())
            {
                return;
            }

            _attackPressTime = Time.time;
            _attackDecisionPending = true;
        }

        private void OnAttackReleased()
        {
            if (!_attackDecisionPending)
            {
                return;
            }

            _attackDecisionPending = false;
            TryStartAttack(false);
        }

        private void OnDodge()
        {
            if (!CanStartAction())
            {
                return;
            }

            StartAction(CombatAction.Dodge, GetDodgeStateName());
        }

        private void OnBlockActivated()
        {
            if (!CanStartAction())
            {
                return;
            }

            if (!_isDrawn)
            {
                _pendingAction = CombatAction.Block;
                StartAction(CombatAction.Draw, "Draw");
                return;
            }

            _blockEnding = false;
            StartAction(CombatAction.Block, "Block_Begin");
        }

        private void OnBlockDeactivated()
        {
            _pendingAction = CombatAction.None;

            if (_currentAction != CombatAction.Block || _blockEnding)
            {
                return;
            }

            _blockEnding = true;
            CrossFadeTo("Block_End");
        }

        private void OnSheatheToggled()
        {
            if (_currentAction != CombatAction.None)
            {
                return;
            }

            _socketSwapped = false;
            StartAction(_isDrawn ? CombatAction.Sheathe : CombatAction.Draw, _isDrawn ? "Sheathe" : "Draw");
        }

        #endregion

        #region Action Flow

        /// <summary>
        ///     Whether a new combat action may start right now. Attacking out of a block is allowed.
        /// </summary>
        private bool CanStartAction()
        {
            return _currentAction == CombatAction.None || _currentAction == CombatAction.Block;
        }

        private void TryStartAttack(bool heavy)
        {
            if (!CanStartAction())
            {
                return;
            }

            if (!_isDrawn)
            {
                _pendingAction = heavy ? CombatAction.HeavyAttack : CombatAction.LightAttack;
                _socketSwapped = false;
                StartAction(CombatAction.Draw, "Draw");
                return;
            }

            _comboIndex = 0;
            _comboQueued = false;
            StartAction(
                heavy ? CombatAction.HeavyAttack : CombatAction.LightAttack,
                heavy ? _HEAVY_STATES[0] : _LIGHT_STATES[0]
            );
        }

        /// <summary>
        ///     Queues the next attack of the current combo if pressed inside the queue window.
        /// </summary>
        private void TryQueueCombo()
        {
            string[] chain = _currentAction == CombatAction.HeavyAttack ? _HEAVY_STATES : _LIGHT_STATES;

            if (_comboIndex >= chain.Length - 1)
            {
                return;
            }

            AnimatorStateInfo stateInfo = GetCombatStateInfo();

            if (stateInfo.IsName(_currentStateName) && stateInfo.normalizedTime >= _comboQueueOpenPoint)
            {
                _comboQueued = true;
            }
        }

        private void StartAction(CombatAction action, string stateName)
        {
            _currentAction = action;
            CrossFadeTo(stateName);

            if (_playerAnimationController != null)
            {
                _playerAnimationController.SetMovementLock(action != CombatAction.None);
            }
        }

        private void EndAction()
        {
            _currentAction = CombatAction.None;
            _blockEnding = false;
            _currentStateName = null;

            if (_playerAnimationController != null)
            {
                _playerAnimationController.SetMovementLock(false);
            }

            if (_pendingAction != CombatAction.None)
            {
                CombatAction pending = _pendingAction;
                _pendingAction = CombatAction.None;

                switch (pending)
                {
                    case CombatAction.LightAttack:
                        TryStartAttack(false);
                        break;
                    case CombatAction.HeavyAttack:
                        TryStartAttack(true);
                        break;
                    case CombatAction.Block:
                        OnBlockActivated();
                        break;
                }
            }
        }

        private void CrossFadeTo(string stateName)
        {
            _currentStateName = stateName;
            _animator.CrossFadeInFixedTime(stateName, _crossFadeDuration, _combatLayer, 0f);
        }

        /// <summary>
        ///     Advances or finishes the current action based on the combat layer's state progress.
        /// </summary>
        private void UpdateCurrentAction()
        {
            if (_currentAction == CombatAction.None)
            {
                return;
            }

            AnimatorStateInfo stateInfo = GetCombatStateInfo();

            if (!stateInfo.IsName(_currentStateName))
            {
                // Still fading towards the requested state (or Block_Begin auto-transitioned to Block_Loop).
                if (_currentAction == CombatAction.Block && !_blockEnding && stateInfo.IsName("Block_Loop"))
                {
                    _currentStateName = "Block_Loop";
                }

                return;
            }

            float progress = stateInfo.normalizedTime;

            switch (_currentAction)
            {
                case CombatAction.LightAttack:
                case CombatAction.HeavyAttack:
                    string[] chain = _currentAction == CombatAction.HeavyAttack ? _HEAVY_STATES : _LIGHT_STATES;

                    if (_comboQueued && progress >= _comboChainPoint)
                    {
                        _comboQueued = false;
                        _comboIndex++;
                        CrossFadeTo(chain[_comboIndex]);
                    }
                    else if (progress >= 0.95f)
                    {
                        EndAction();
                    }

                    break;

                case CombatAction.Draw:
                case CombatAction.Sheathe:
                    if (!_socketSwapped && progress >= 0.5f)
                    {
                        _socketSwapped = true;
                        bool drawing = _currentAction == CombatAction.Draw;
                        PlaceSwordInSocket(drawing ? _handSocket : _backSocket);
                    }

                    if (progress >= 0.95f)
                    {
                        _isDrawn = _currentAction == CombatAction.Draw;
                        EndAction();
                    }

                    break;

                case CombatAction.Dodge:
                    if (progress >= 0.95f)
                    {
                        EndAction();
                    }

                    break;

                case CombatAction.Block:
                    if (_blockEnding && stateInfo.IsName("Block_End") && progress >= 0.9f)
                    {
                        EndAction();
                    }

                    break;
            }
        }

        /// <summary>
        ///     Gets the state info of the combat layer, preferring the state being faded towards.
        /// </summary>
        private AnimatorStateInfo GetCombatStateInfo()
        {
            return _animator.IsInTransition(_combatLayer)
                ? _animator.GetNextAnimatorStateInfo(_combatLayer)
                : _animator.GetCurrentAnimatorStateInfo(_combatLayer);
        }

        /// <summary>
        ///     Picks the dodge state matching the current movement input; defaults to backwards.
        /// </summary>
        private string GetDodgeStateName()
        {
            Vector2 input = _inputReader._moveComposite;

            if (input.magnitude < 0.1f)
            {
                return "Dodge_B";
            }

            if (Mathf.Abs(input.x) > Mathf.Abs(input.y))
            {
                return input.x > 0 ? "Dodge_R" : "Dodge_L";
            }

            return input.y > 0 ? "Dodge_F" : "Dodge_B";
        }

        #endregion

        #region Sword and Layer Weights

        private void PlaceSwordInSocket(Transform socket)
        {
            if (_sword == null || socket == null)
            {
                return;
            }

            _sword.transform.SetParent(socket, false);
            _sword.transform.localPosition = Vector3.zero;
            _sword.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        ///     Fades the combat layer in while an action plays, and the sword-pose mask layers in
        ///     while the sword is drawn.
        /// </summary>
        private void UpdateLayerWeights()
        {
            SetLayerWeightSmooth(_combatLayer, _currentAction != CombatAction.None ? 1f : 0f);
            SetSwordLayerWeights(_isDrawn ? 1f : 0f, false);
        }

        private void SetSwordLayerWeights(float drawnFactor, bool instant)
        {
            if (_swordArmLayer >= 0)
            {
                float target = _armLayerWeight * drawnFactor;
                if (instant)
                {
                    _animator.SetLayerWeight(_swordArmLayer, target);
                }
                else
                {
                    SetLayerWeightSmooth(_swordArmLayer, target);
                }
            }

            if (_swordHandLayer >= 0)
            {
                if (instant)
                {
                    _animator.SetLayerWeight(_swordHandLayer, drawnFactor);
                }
                else
                {
                    SetLayerWeightSmooth(_swordHandLayer, drawnFactor);
                }
            }
        }

        private void SetLayerWeightSmooth(int layerIndex, float target)
        {
            float current = _animator.GetLayerWeight(layerIndex);
            _animator.SetLayerWeight(layerIndex, Mathf.Lerp(current, target, _layerWeightFadeSpeed * Time.deltaTime));
        }

        #endregion
    }
}
