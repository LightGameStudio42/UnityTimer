/*
 * Unity Timer
 *
 * Version: 1.0
 * By: Alexander Biggs + Adam Robinson-Yu
 */

using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using JetBrains.Annotations;
using Object = UnityEngine.Object;

/// <summary>
/// Allows you to run events on a delay without the use of <see cref="Coroutine"/>s
/// or <see cref="MonoBehaviour"/>s.
///
/// To create and start a Timer, use the <see cref="Register"/> method.
/// </summary>
namespace UnityTimer
{
    public class Timer
    {
        #region Public Properties/Fields

        /// <summary>
        /// How long the timer takes to complete from start to finish.
        /// </summary>
        public float duration { get; private set; }

        /// <summary>
        /// Whether the timer will run again after completion.
        /// </summary>
        public bool isLooped { get; set; }

        /// <summary>
        /// Whether or not the timer completed running. This is false if the timer was cancelled.
        /// </summary>
        public bool isCompleted { get; private set; }

        /// <summary>
        /// Whether the timer uses real-time or game-time. Real time is unaffected by changes to the timescale
        /// of the game(e.g. pausing, slow-mo), while game time is affected.
        /// </summary>
        public bool usesRealTime { get; private set; }

        /// <summary>
        /// Whether the timer is currently paused.
        /// </summary>
        public bool isPaused => timeElapsedBeforePause.HasValue;


        /// <summary>
        /// Whether or not the timer was cancelled.
        /// </summary>
        public bool isCancelled => timeElapsedBeforeCancel.HasValue;


        /// <summary>
        /// Get whether or not the timer has finished running for any reason.
        /// </summary>
        public bool isDone
        {
            get { return this.isCompleted || this.isCancelled || this.isOwnerDestroyed; }
        }

        #endregion

        #region Public Static Methods

        /// <summary>
        /// Register a new timer that should fire an event after a certain amount of time
        /// has elapsed.
        ///
        /// Registered timers are destroyed when the scene changes.
        /// </summary>
        /// <param name="duration">The time to wait before the timer should fire, in seconds.</param>
        /// <param name="onComplete">An action to fire when the timer completes.</param>
        /// <param name="onUpdate">An action that should fire each time the timer is updated. Takes the amount
        /// of time passed in seconds since the start of the timer's current loop.</param>
        /// <param name="isLooped">Whether the timer should repeat after executing.</param>
        /// <param name="useRealTime">Whether the timer uses real-time(i.e. not affected by pauses,
        /// slow/fast motion) or game-time(will be affected by pauses and slow/fast-motion).</param>
        /// <param name="autoDestroyOwner">An object to attach this timer to. After the object is destroyed,
        /// the timer will expire and not execute. This allows you to avoid annoying <see cref="NullReferenceException"/>s
        /// by preventing the timer from running and accessessing its parents' components
        /// after the parent has been destroyed.</param>
        /// <returns>A timer object that allows you to examine stats and stop/resume progress.</returns>
        public static Timer Register(float duration, Action onComplete, Action<float> onUpdate = null,
            bool isLooped = false, bool useRealTime = false, MonoBehaviour autoDestroyOwner = null)
        {
            // create a manager object to update all the timers if one does not already exist.
            if (Timer.manager == null)
            {
                var managerInScene = Object.FindObjectOfType<TimerManager>();
                if (managerInScene != null)
                {
                    Timer.manager = managerInScene;
                }
                else
                {
                    GameObject managerObject = new() { name = "TimerManager" };
                    Timer.manager = managerObject.AddComponent<TimerManager>();
                }
            }

            var timer = new Timer(duration, onComplete, onUpdate, isLooped, useRealTime, autoDestroyOwner);
            Timer.manager.RegisterTimer(timer);
            return timer;
        }

        /// <summary>
        /// Cancels a timer. The main benefit of this over the method on the instance is that you will not get
        /// a <see cref="NullReferenceException"/> if the timer is null.
        /// </summary>
        /// <param name="timer">The timer to cancel.</param>
        public static void Cancel(Timer timer)
        {
            if (timer != null)
            {
                timer.Cancel();
            }
        }

        /// <summary>
        /// Pause a timer. The main benefit of this over the method on the instance is that you will not get
        /// a <see cref="NullReferenceException"/> if the timer is null.
        /// </summary>
        /// <param name="timer">The timer to pause.</param>
        public static void Pause(Timer timer)
        {
            if (timer != null)
            {
                timer.Pause();
            }
        }

        /// <summary>
        /// Resume a timer. The main benefit of this over the method on the instance is that you will not get
        /// a <see cref="NullReferenceException"/> if the timer is null.
        /// </summary>
        /// <param name="timer">The timer to resume.</param>
        public static void Resume(Timer timer)
        {
            if (timer != null)
            {
                timer.Resume();
            }
        }

        public static void CancelAllRegisteredTimers()
        {
            if (Timer.manager != null)
            {
                Timer.manager.CancelAllTimers();
            }

            // if the manager doesn't exist, we don't have any registered timers yet, so don't
            // need to do anything in this case
        }

        public static void PauseAllRegisteredTimers()
        {
            if (Timer.manager != null)
            {
                Timer.manager.PauseAllTimers();
            }

            // if the manager doesn't exist, we don't have any registered timers yet, so don't
            // need to do anything in this case
        }

        public static void ResumeAllRegisteredTimers()
        {
            if (Timer.manager != null)
            {
                Timer.manager.ResumeAllTimers();
            }

            // if the manager doesn't exist, we don't have any registered timers yet, so don't
            // need to do anything in this case
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Stop a timer that is in-progress or paused. The timer's on completion callback will not be called.
        /// </summary>
        public void Cancel()
        {
            if (isDone)
            {
                return;
            }

            timeElapsedBeforeCancel = GetTimeElapsed();
            timeElapsedBeforePause = null;
        }

        /// <summary>
        /// Pause a running timer. A paused timer can be resumed from the same point it was paused.
        /// </summary>
        public void Pause()
        {
            if (isPaused || isDone)
            {
                return;
            }

            timeElapsedBeforePause = GetTimeElapsed();
        }

        /// <summary>
        /// Continue a paused timer. Does nothing if the timer has not been paused.
        /// </summary>
        public void Resume()
        {
            if (!isPaused || isDone)
            {
                return;
            }

            timeElapsedBeforePause = null;
        }

        /// <summary>
        /// Get how many seconds have elapsed since the start of this timer's current cycle.
        /// </summary>
        /// <returns>The number of seconds that have elapsed since the start of this timer's current cycle, i.e.
        /// the current loop if the timer is looped, or the start if it isn't.
        ///
        /// If the timer has finished running, this is equal to the duration.
        ///
        /// If the timer was cancelled/paused, this is equal to the number of seconds that passed between the timer
        /// starting and when it was cancelled/paused.</returns>
        public float GetTimeElapsed()
        {
            if (isCompleted || GetWorldTime() >= GetFireTime())
            {
                return duration;
            }

            return timeElapsedBeforeCancel ??
                   timeElapsedBeforePause ??
                   GetWorldTime() - startTime;
        }

        /// <summary>
        /// Get how many seconds remain before the timer completes.
        /// </summary>
        /// <returns>The number of seconds that remain to be elapsed until the timer is completed. A timer
        /// is only elapsing time if it is not paused, cancelled, or completed. This will be equal to zero
        /// if the timer completed.</returns>
        public float GetTimeRemaining() => duration - GetTimeElapsed();


        /// <summary>
        /// Get how much progress the timer has made from start to finish as a ratio.
        /// </summary>
        /// <returns>A value from 0 to 1 indicating how much of the timer's duration has been elapsed.</returns>
        public float GetRatioComplete() => GetTimeElapsed() / duration;


        /// <summary>
        /// Get how much progress the timer has left to make as a ratio.
        /// </summary>
        /// <returns>A value from 0 to 1 indicating how much of the timer's duration remains to be elapsed.</returns>
        public float GetRatioRemaining() => GetTimeRemaining() / duration;


        #endregion

        #region Private Static Properties/Fields

        // responsible for updating all registered timers
        private static TimerManager manager;

        #endregion

        #region Private Properties/Fields

        private bool isOwnerDestroyed => hasAutoDestroyOwner && autoDestroyOwner == null;


        private readonly Action onComplete;
        private readonly Action<float> onUpdate;
        private float startTime;
        private float lastUpdateTime;

        // for pausing, we push the start time forward by the amount of time that has passed.
        // this will mess with the amount of time that elapsed when we're cancelled or paused if we just
        // check the start time versus the current world time, so we need to cache the time that was elapsed
        // before we paused/cancelled
        private float? timeElapsedBeforeCancel;
        private float? timeElapsedBeforePause;

        // after the auto destroy owner is destroyed, the timer will expire
        // this way you don't run into any annoying bugs with timers running and accessing objects
        // after they have been destroyed
        private readonly MonoBehaviour autoDestroyOwner;
        private readonly bool hasAutoDestroyOwner;

        #endregion

        #region Private Constructor (use static Register method to create new timer)

        private Timer(float duration, Action onComplete, Action<float> onUpdate,
            bool isLooped, bool usesRealTime, MonoBehaviour autoDestroyOwner)
        {
            this.duration = duration;
            this.onComplete = onComplete;
            this.onUpdate = onUpdate;

            this.isLooped = isLooped;
            this.usesRealTime = usesRealTime;

            this.autoDestroyOwner = autoDestroyOwner;
            this.hasAutoDestroyOwner = autoDestroyOwner != null;

            this.startTime = this.GetWorldTime();
            this.lastUpdateTime = this.startTime;
        }

        #endregion

        #region Private Methods

        private float GetWorldTime() => usesRealTime ? Time.realtimeSinceStartup : Time.time;

        private float GetFireTime() => startTime + duration;

        private float GetTimeDelta() => GetWorldTime() - lastUpdateTime;


        private void Update()
        {
            if (isDone)
            {
                return;
            }

            if (isPaused)
            {
                startTime += GetTimeDelta();
                lastUpdateTime = GetWorldTime();
                return;
            }

            lastUpdateTime = GetWorldTime();

            if (onUpdate != null)
            {
                onUpdate(GetTimeElapsed());
            }

            if (GetWorldTime() >= GetFireTime())
            {
                if (onComplete != null)
                {
                    onComplete();
                }

                if (isLooped)
                {
                    startTime = GetWorldTime();
                }
                else
                {
                    isCompleted = true;
                }
            }
        }

        #endregion

        #region Manager Class (implementation detail, spawned automatically and updates all registered timers)

        /// <summary>
        /// Manages updating all the <see cref="Timer"/>s that are running in the application.
        /// This will be instantiated the first time you create a timer -- you do not need to add it into the
        /// scene manually.
        /// </summary>
        private class TimerManager : MonoBehaviour
        {
            private List<Timer> timers = new();

            // buffer adding timers so we don't edit a collection during iteration
            private List<Timer> timersToAdd = new();

            public void RegisterTimer(Timer timer)
            {
                timersToAdd.Add(timer);
            }

            public void CancelAllTimers()
            {
                foreach (var timer in timers)
                {
                    timer.Cancel();
                }

                timers = new();
                timersToAdd = new();
            }

            public void PauseAllTimers()
            {
                timers.ForEach(Pause);
            }

            public void ResumeAllTimers()
            {
                timers.ForEach(Resume);
            }

            // update all the registered timers on every frame
            [UsedImplicitly]
            private void Update()
            {
                UpdateAllTimers();
            }

            private void UpdateAllTimers()
            {
                if (timersToAdd.Count > 0)
                {
                    timers.AddRange(timersToAdd);
                    timersToAdd.Clear();
                }

                timers.ForEach(t => t.Update());

                timers.RemoveAll(t => t.isDone);
            }
        }

        #endregion

    }
}
