// Copyright (c) 2024 Synty Studios Limited. All rights reserved.
//
// Use of this software is subject to the terms and conditions of the Synty Studios End User Licence Agreement (EULA)
// available at: https://syntystore.com/pages/end-user-licence-agreement
//
// Adapted from the Synty sample scripts for this project.

using Dungeon.InputSystem;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    ///     Handles lock-on targeting for the player: tracks candidate targets in range, scores the
    ///     best candidate, toggles the lock-on state from input, and keeps the lock-on anchor on the
    ///     current target. Extracted from the Synty sample player animation controller.
    /// </summary>
    public class LockOnController : MonoBehaviour
    {
        [Header("External Components")]
        [Tooltip("Script controlling camera behavior; used for target scoring and camera lock-on")]
        [SerializeField]
        private CameraController _cameraController;
        [Tooltip("InputReader handles player input")]
        [SerializeField]
        private InputReader _inputReader;

        [Header("Target Scoring")]
        [Tooltip("Weighting applied to target distance when scoring candidates")]
        [SerializeField]
        private float _distanceWeight = 100f;
        [Tooltip("Weighting applied to camera view angle when scoring candidates")]
        [SerializeField]
        private float _angleWeight = 40f;

        private readonly List<GameObject> _currentTargetCandidates = new List<GameObject>();
        private GameObject _currentLockOnTarget;
        private Transform _targetLockOnPos;
        private bool _isLockedOn;

        /// <summary>
        ///     Raised whenever the lock-on state changes, with the new state.
        /// </summary>
        public event Action<bool> LockedOnChanged;

        /// <summary>
        ///     Whether the player is currently locked on to a target.
        /// </summary>
        public bool IsLockedOn => _isLockedOn;

        /// <summary>
        ///     The current lock-on target, or the best candidate when not locked on.
        /// </summary>
        public GameObject CurrentTarget => _currentLockOnTarget;

        /// <inheritdoc cref="Start" />
        private void Start()
        {
            _targetLockOnPos = transform.Find("TargetLockOnPos");

            _inputReader.onLockOnToggled += ToggleLockOn;
        }

        /// <inheritdoc cref="OnDestroy" />
        private void OnDestroy()
        {
            if (_inputReader != null)
            {
                _inputReader.onLockOnToggled -= ToggleLockOn;
            }
        }

        /// <inheritdoc cref="Update" />
        private void Update()
        {
            UpdateBestTarget();

            if (_isLockedOn && _currentLockOnTarget != null)
            {
                _targetLockOnPos.position = _currentLockOnTarget.transform.position;
            }
        }

        /// <summary>
        ///     Adds an object to the list of target candidates.
        /// </summary>
        /// <param name="newTarget">The object to add.</param>
        public void AddTargetCandidate(GameObject newTarget)
        {
            if (newTarget != null)
            {
                _currentTargetCandidates.Add(newTarget);
            }
        }

        /// <summary>
        ///     Removes an object to the list of target candidates if present.
        /// </summary>
        /// <param name="targetToRemove">The object to remove if present.</param>
        public void RemoveTarget(GameObject targetToRemove)
        {
            if (_currentTargetCandidates.Contains(targetToRemove))
            {
                _currentTargetCandidates.Remove(targetToRemove);
            }
        }

        /// <summary>
        ///     Toggle the lock-on state.
        /// </summary>
        private void ToggleLockOn()
        {
            EnableLockOn(!_isLockedOn);
        }

        /// <summary>
        ///     Sets the lock-on state to the given state.
        /// </summary>
        /// <param name="enable">The state to set lock-on to.</param>
        private void EnableLockOn(bool enable)
        {
            _isLockedOn = enable;

            _cameraController.LockOn(enable, _targetLockOnPos);

            if (enable && _currentLockOnTarget != null)
            {
                _currentLockOnTarget.GetComponent<ObjectLockOn>().Highlight(true, true);
            }

            LockedOnChanged?.Invoke(enable);
        }

        /// <summary>
        ///     Updates and sets the best target for lock on from the list of available targets.
        /// </summary>
        private void UpdateBestTarget()
        {
            GameObject newBestTarget;

            if (_currentTargetCandidates.Count == 0)
            {
                newBestTarget = null;
            }
            else if (_currentTargetCandidates.Count == 1)
            {
                newBestTarget = _currentTargetCandidates[0];
            }
            else
            {
                newBestTarget = null;
                float bestTargetScore = 0f;

                foreach (GameObject target in _currentTargetCandidates)
                {
                    target.GetComponent<ObjectLockOn>().Highlight(false, false);

                    float distance = Vector3.Distance(transform.position, target.transform.position);
                    float distanceScore = 1 / distance * _distanceWeight;

                    Vector3 targetDirection = target.transform.position - _cameraController.GetCameraPosition();
                    float angleInView = Vector3.Dot(targetDirection.normalized, _cameraController.GetCameraForward());
                    float angleScore = angleInView * _angleWeight;

                    float totalScore = distanceScore + angleScore;

                    if (totalScore > bestTargetScore)
                    {
                        bestTargetScore = totalScore;
                        newBestTarget = target;
                    }
                }
            }

            if (!_isLockedOn)
            {
                _currentLockOnTarget = newBestTarget;

                if (_currentLockOnTarget != null)
                {
                    _currentLockOnTarget.GetComponent<ObjectLockOn>().Highlight(true, false);
                }
            }
            else
            {
                if (_currentTargetCandidates.Contains(_currentLockOnTarget))
                {
                    _currentLockOnTarget.GetComponent<ObjectLockOn>().Highlight(true, true);
                }
                else
                {
                    _currentLockOnTarget = newBestTarget;
                    EnableLockOn(false);
                }
            }
        }
    }
}
