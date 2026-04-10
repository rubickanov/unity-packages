using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// IMGUI connection panel. Drop on any GameObject in the scene.
    /// Player prefab spawning is handled by NetworkManager (set Player Prefab in inspector).
    /// </summary>
    public class NetworkExperimentBootstrap : MonoBehaviour
    {
        private string _ip = "127.0.0.1";
        private string _port = "7777";
        private bool _showPanel = true;

        private void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab)
            {
                _showPanel = !_showPanel;
                Event.current.Use();
            }

            if (!_showPanel)
            {
                GUI.Label(new Rect(10, 10, 200, 20), "<size=11><color=#888>[Tab] Show panel</color></size>");
                return;
            }

            var nm = NetworkManager.Singleton;
            bool connected = nm != null && nm.IsListening;

            float panelHeight = connected ? 100 : 180;
            GUI.Box(new Rect(8, 8, 264, panelHeight), "");
            GUILayout.BeginArea(new Rect(12, 12, 256, panelHeight - 4));

            var headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            GUILayout.Label("ACS Netcode Experiment", headerStyle);
            GUILayout.Space(4);

            if (!connected)
                DrawConnectionUI(nm);
            else
                DrawSessionUI(nm);

            GUILayout.EndArea();
        }

        private void DrawConnectionUI(NetworkManager? nm)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("IP:", GUILayout.Width(30));
            _ip = GUILayout.TextField(_ip, GUILayout.Width(130));
            GUILayout.Label("Port:", GUILayout.Width(35));
            _port = GUILayout.TextField(_port, 5, GUILayout.Width(50));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (GUILayout.Button("Host", GUILayout.Height(30)))
            {
                ApplyTransportSettings(nm);
                nm?.StartHost();
            }

            GUILayout.Space(2);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Client", GUILayout.Height(30)))
            {
                ApplyTransportSettings(nm);
                nm?.StartClient();
            }
            if (GUILayout.Button("Server", GUILayout.Height(30)))
            {
                ApplyTransportSettings(nm);
                nm?.StartServer();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSessionUI(NetworkManager nm)
        {
            string role = nm.IsHost ? "HOST" : nm.IsServer ? "SERVER" : "CLIENT";
            var statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            GUILayout.Label($"<b>{role}</b>  |  Clients: {nm.ConnectedClientsList?.Count ?? 0}  |  Id: {nm.LocalClientId}", statusStyle);

            GUILayout.Space(4);
            if (GUILayout.Button("Shutdown", GUILayout.Height(26)))
                nm.Shutdown();
        }

        private void ApplyTransportSettings(NetworkManager? nm)
        {
            if (nm == null) return;
            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null) return;

            transport.ConnectionData.Address = _ip;
            if (ushort.TryParse(_port, out ushort p))
                transport.ConnectionData.Port = p;
        }
    }
}
