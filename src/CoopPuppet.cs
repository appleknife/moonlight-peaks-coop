using System;
using System.Reflection;
using UnityEngine;

namespace MoonlightPeaksCoop
{
    internal sealed class CoopPuppet : MonoBehaviour
    {
        private PlayerView view;
        private Vector3 targetPos;
        private Quaternion targetRot;
        private float targetSpeed;
        private float targetState;
        private float lastPacket;
        private AnimatorParameterAsset speedParameter;
        private CharacterController controller;
        private static int logCount;

        private string heldGuid = "";
        private int heldQuality;
        private string heldMetadata;
        private string heldCustomName;
        private ItemEntry lastHeldEntry;
        private bool heldDirty;
        private float nextHeldAttempt;
        private float nextHeldCheck;
        private static readonly MethodInfo HandleGrabbed = typeof(PlayerEquipment).GetMethod("HandleGrabbedItemChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal void Setup(PlayerView v)
        {
            view = v;
            controller = v.Mover ? v.Mover.Controller : null;
            if (controller) controller.enabled = false;
            if (v.Mover) v.Mover.enabled = false;
            if (v.Input) v.Input.enabled = false;
            if (v.Interactor) v.Interactor.enabled = false;
            if (v.ToolInteractor) v.ToolInteractor.enabled = false;
            if (v.StateMachine) v.StateMachine.enabled = false;
            if (v.Mover)
                speedParameter = typeof(CharacterMover).GetField("animatorSpeedParameterAsset", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(v.Mover) as AnimatorParameterAsset;
            targetPos = v.Character.transform.position;
            targetRot = v.Character.transform.rotation;
            lastPacket = Time.unscaledTime;
        }

        internal void ApplyHeld(string guid, int quality, string metadata, string customName)
        {
            var g = guid ?? "";
            var changed = g != heldGuid || heldQuality != quality || heldMetadata != metadata || heldCustomName != customName;
            heldGuid = g;
            heldQuality = quality;
            heldMetadata = metadata;
            heldCustomName = customName;
            if (!changed) return;
            heldDirty = true;
            nextHeldAttempt = 0f;
        }

        private void ApplyHeldNow()
        {
            heldDirty = false;
            nextHeldAttempt = Time.unscaledTime + 0.5f;
            try
            {
                var equipment = view.Equipment;
                if (!equipment) { heldDirty = true; return; }
                var info = new GrabbedItemChangeInfo();
                info.PreviousItemEntry = lastHeldEntry;
                info.Instant = true;
                if (heldGuid.Length > 0)
                {
                    var asset = Asset.Get<ItemAsset>(SerializedGuid.FromString(heldGuid));
                    if (!asset) { heldDirty = true; return; }
                    info.ItemEntry = new ItemEntry(asset, (ItemQualityLevel)heldQuality, heldMetadata, heldCustomName);
                }
                lastHeldEntry = info.ItemEntry;
                HandleGrabbed.Invoke(equipment, new object[] { info });
            }
            catch (Exception e)
            {
                heldDirty = true;
                CoopPlugin.Log.LogWarning("held item: " + e.Message);
            }
        }

        internal void Apply(float x, float y, float z, float rx, float ry, float rz, float rw, float speed, float state)
        {
            targetPos = new Vector3(x, y, z);
            targetRot = new Quaternion(rx, ry, rz, rw);
            targetSpeed = speed;
            targetState = state;
            lastPacket = Time.unscaledTime;
            if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
            if (!view.Character.gameObject.activeSelf) view.Character.gameObject.SetActive(true);
            view.Character.transform.position = targetPos;
            view.Character.transform.rotation = targetRot;
            if (logCount++ % 120 == 0) CoopPlugin.Log.LogInfo("puppet apply " + view.name + " active=" + view.gameObject.activeInHierarchy + " actual=" + view.Character.transform.position);
        }

        private void Update()
        {
            if (!view || !view.Character) return;
            var t = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            view.Character.transform.position = Vector3.Lerp(view.Character.transform.position, targetPos, t);
            view.Character.transform.rotation = Quaternion.Slerp(view.Character.transform.rotation, targetRot, t);
            if (view.Animator && speedParameter && view.Animator.Animator)
                view.Animator.SetFloat(speedParameter, targetSpeed);
            if (heldDirty && Time.unscaledTime >= nextHeldAttempt) ApplyHeldNow();
            else if (!heldDirty && heldGuid.Length > 0 && Time.unscaledTime >= nextHeldCheck)
            {
                nextHeldCheck = Time.unscaledTime + 1f;
                if (!view.Equipment || !view.Equipment.GrabbedItemView) heldDirty = true;
            }
            var visible = Time.unscaledTime - lastPacket < 5f;
            if (view.Character.gameObject.activeSelf != visible)
                view.Character.gameObject.SetActive(visible);
        }
    }
}




