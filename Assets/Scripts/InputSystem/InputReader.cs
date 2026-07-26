// Copyright (c) 2024 Synty Studios Limited. All rights reserved.
//
// Use of this software is subject to the terms and conditions of the Synty Studios End User Licence Agreement (EULA)
// available at: https://syntystore.com/pages/end-user-licence-agreement
//
// Adapted from the Synty sample scripts for this project.

using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dungeon.InputSystem
{
    public class InputReader : MonoBehaviour, DungeonControls.IPlayerActions
    {
        public Vector2 _mouseDelta;
        public Vector2 _moveComposite;

        public float _movementInputDuration;
        public bool _movementInputDetected;

        private DungeonControls _controls;

        public Action onBlockActivated;
        public Action onBlockDeactivated;

        public Action onCrouchActivated;
        public Action onCrouchDeactivated;

        public Action onJumpPerformed;

        public Action onLockOnToggled;

        public Action onSprintActivated;
        public Action onSprintDeactivated;

        public Action onWalkToggled;

        public Action onAttackPressed;
        public Action onAttackReleased;

        public Action onDodgePerformed;

        public Action onSheatheToggled;

        /// <inheritdoc cref="OnEnable" />
        private void OnEnable()
        {
            if (_controls == null)
            {
                _controls = new DungeonControls();
                _controls.Player.SetCallbacks(this);
            }

            _controls.Player.Enable();
        }

        /// <inheritdoc cref="OnDisable" />
        public void OnDisable()
        {
            _controls.Player.Disable();
        }

        /// <summary>
        ///     Defines the action to perform when the OnLook callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnLook(InputAction.CallbackContext context)
        {
            _mouseDelta = context.ReadValue<Vector2>();
        }

        /// <summary>
        ///     Defines the action to perform when the OnMove callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnMove(InputAction.CallbackContext context)
        {
            _moveComposite = context.ReadValue<Vector2>();
            _movementInputDetected = _moveComposite.magnitude > 0;
        }

        /// <summary>
        ///     Defines the action to perform when the OnJump callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnJump(InputAction.CallbackContext context)
        {
            if (!context.performed)
            {
                return;
            }

            onJumpPerformed?.Invoke();
        }

        /// <summary>
        ///     Defines the action to perform when the OnToggleWalk callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnToggleWalk(InputAction.CallbackContext context)
        {
            if (!context.performed)
            {
                return;
            }

            onWalkToggled?.Invoke();
        }

        /// <summary>
        ///     Defines the action to perform when the OnSprint callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnSprint(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                onSprintActivated?.Invoke();
            }
            else if (context.canceled)
            {
                onSprintDeactivated?.Invoke();
            }
        }

        /// <summary>
        ///     Defines the action to perform when the OnCrouch callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnCrouch(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                onCrouchActivated?.Invoke();
            }
            else if (context.canceled)
            {
                onCrouchDeactivated?.Invoke();
            }
        }

        /// <summary>
        ///     Defines the action to perform when the OnBlock callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnBlock(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                onBlockActivated?.Invoke();
            }

            if (context.canceled)
            {
                onBlockDeactivated?.Invoke();
            }
        }

        /// <summary>
        ///     Defines the action to perform when the OnAttack callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnAttack(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                onAttackPressed?.Invoke();
            }
            else if (context.canceled)
            {
                onAttackReleased?.Invoke();
            }
        }

        /// <summary>
        ///     Defines the action to perform when the OnDodge callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnDodge(InputAction.CallbackContext context)
        {
            if (!context.performed)
            {
                return;
            }

            onDodgePerformed?.Invoke();
        }

        /// <summary>
        ///     Defines the action to perform when the OnToggleSheathe callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnToggleSheathe(InputAction.CallbackContext context)
        {
            if (!context.performed)
            {
                return;
            }

            onSheatheToggled?.Invoke();
        }

        /// <summary>
        ///     Defines the action to perform when the OnLockOn callback is called.
        /// </summary>
        /// <param name="context">The context of the callback.</param>
        public void OnLockOn(InputAction.CallbackContext context)
        {
            if (!context.performed)
            {
                return;
            }

            onLockOnToggled?.Invoke();
            onSprintDeactivated?.Invoke();
        }
    }
}
