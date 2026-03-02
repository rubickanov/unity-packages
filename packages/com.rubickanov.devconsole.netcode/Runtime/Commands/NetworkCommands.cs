using Rubickanov.DevConsole;
using Unity.Netcode;

namespace Rubickanov.DevConsole.Netcode.Commands
{
    internal static class NetworkCommands
    {
        [ConsoleCommand("status", "Show network connection status", "Network")]
        [CommandDomain(CommandDomain.Shared)]
        public static void Status()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                ConsoleLog.Log("Not connected.");
                return;
            }

            var role = nm.IsHost ? "Host" : nm.IsServer ? "Server" : "Client";
            ConsoleLog.Log($"Role: {role}");
            ConsoleLog.Log($"Transport: {nm.NetworkConfig.NetworkTransport.GetType().Name}");
            ConsoleLog.Log($"Connected clients: {nm.ConnectedClientsList.Count}");
            ConsoleLog.Log($"Local client ID: {nm.LocalClientId}");
        }

        [ConsoleCommand("players", "List connected players", "Network")]
        [CommandDomain(CommandDomain.Shared)]
        public static void Players()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                ConsoleLog.LogError("Not connected.");
                return;
            }

            foreach (var client in nm.ConnectedClientsList)
            {
                var local = client.ClientId == nm.LocalClientId ? " (local)" : "";
                ConsoleLog.Log($"  Client {client.ClientId}{local}");
            }
        }

        [ConsoleCommand("ping", "Show approximate round-trip time", "Network")]
        [CommandDomain(CommandDomain.Client)]
        public static void Ping()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                ConsoleLog.LogError("Not connected.");
                return;
            }

            // Approximate RTT from local vs server time difference
            var localTime = nm.LocalTime.TimeAsFloat;
            var serverTime = nm.ServerTime.TimeAsFloat;
            var rtt = (localTime - serverTime) * 2f * 1000f;
            ConsoleLog.Log($"Estimated RTT: {rtt:F1} ms (local: {localTime:F3}, server: {serverTime:F3})");
        }

        [ConsoleCommand("net_stats", "Show network statistics", "Network")]
        [CommandDomain(CommandDomain.Shared)]
        public static void NetStats()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                ConsoleLog.LogError("Not connected.");
                return;
            }

            ConsoleLog.Log($"Transport: {nm.NetworkConfig.NetworkTransport.GetType().Name}");
            ConsoleLog.Log($"Connected clients: {nm.ConnectedClientsList.Count}");
            ConsoleLog.Log($"Local client ID: {nm.LocalClientId}");

            var role = nm.IsHost ? "Host" : nm.IsServer ? "Server" : "Client";
            ConsoleLog.Log($"Role: {role}");
        }

        [ConsoleCommand("kick", "Kick a client by ID (server only)", "Network")]
        [CommandDomain(CommandDomain.Server)]
        public static void Kick(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer)
            {
                ConsoleLog.LogError("Not a server.");
                return;
            }

            if (clientId == nm.LocalClientId)
            {
                ConsoleLog.LogError("Cannot kick yourself.");
                return;
            }

            nm.DisconnectClient(clientId);
            ConsoleLog.LogSuccess($"Kicked client {clientId}.");
        }

        [ConsoleCommand("disconnect", "Disconnect from the network session", "Network")]
        [CommandDomain(CommandDomain.Client)]
        public static void Disconnect()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                ConsoleLog.LogError("Not connected.");
                return;
            }

            nm.Shutdown();
            ConsoleLog.LogSuccess("Disconnected.");
        }
    }
}
