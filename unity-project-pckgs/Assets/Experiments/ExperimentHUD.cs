using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Runs on all peers. Shows replicated values as on-screen debug overlay.
    /// Subscribes to events and logs them.
    /// </summary>
    public class ExperimentHUD : EntityNetworkComponent
    {
        [Aspect] private ExperimentAspect _aspect = default!;

        private string _lastEvent = "";
        private float _eventDisplayTimer;

        protected override void OnSubscribe(ref DisposableBag disposables)
        {
            // Server-only: start tracking per-entity payload bytes so OnGUI can show
            // how many bytes the last tick shipped for this entity. Idempotent.
            if (IsServer) PayloadByteTracker.EnsureWired();

            _aspect.DamageDealt.Subscribe(dmg =>
            {
                _lastEvent = $"Damage: -{dmg}";
                _eventDisplayTimer = 2f;
                // Debug.Log($"[{PeerLabel()}] Event: DamageDealt({dmg})");
            }).AddTo(ref disposables);

            _aspect.Footstep.Subscribe(step =>
            {
                _lastEvent = $"Footstep #{step}";
                _eventDisplayTimer = 1f;
                // Debug.Log($"[{PeerLabel()}] Event: Footstep({step})");
            }).AddTo(ref disposables);

            _aspect.Health.Subscribe(hp =>
            {
                //                Debug.Log($"[{PeerLabel()}] Health changed: {hp:F1}");
            }).AddTo(ref disposables);
        }

        private void Update()
        {
            if (_eventDisplayTimer > 0f)
                _eventDisplayTimer -= Time.deltaTime;
        }

        private void LateUpdate()
        {
            // Sync transform after AspectReplicator.Update() has run TickRender,
            // so .Smooth() reads the freshest interpolated value for this frame.
            transform.position = _aspect.Position.Smooth();
            transform.rotation = _aspect.Rotation.Smooth();
        }

        private void OnGUI()
        {
            if (!IsSpawned) return;

            var screenPos = Camera.main != null
                ? Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2f)
                : Vector3.zero;

            if (screenPos.z < 0) return;

            float y = Screen.height - screenPos.y;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            string role = IsOwner ? "OWNER" : IsServer ? "SERVER" : "CLIENT";
            string info = $"[{role}] ObjId:{NetworkObjectId}\n" +
                          $"Pos: {_aspect.Position.Value:F1}\n" +
                          $"HP: {_aspect.Health.Value:F0}\n" +
                          $"Rot: {_aspect.Rotation.Value.eulerAngles.y:F0}°";

            // Server-only: last tick's per-entity body size (serverTick + mask + field bytes,
            // excluding the 8B NetworkObjectId + 2B payloadBytes framing). Compare this with
            // quantization off vs on to see the compression effect directly.
            if (IsServer && PayloadByteTracker.TryGetLast(NetworkObjectId, out int lastPayloadBytes))
                info += $"\n<color=cyan>Payload: {lastPayloadBytes}B</color>";

            if (_eventDisplayTimer > 0f)
                info += $"\n<color=yellow>{_lastEvent}</color>";

            GUI.Label(new Rect(screenPos.x - 120, y - 50, 240, 100), info, style);
        }

        private string PeerLabel()
        {
            if (IsHost) return "Host";
            if (IsServer) return "Server";
            if (IsOwner) return "Owner";
            return "Client";
        }
    }
}
