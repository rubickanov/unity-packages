using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.DevConsole.Netcode
{
    /// <summary>
    /// Bridges DevConsole with Netcode, adding CS:GO-style command domains and cheat protection.
    /// Attach to a NetworkObject that exists for the lifetime of the session (e.g. a network manager).
    /// Without this component, the console works in local/singleplayer mode as usual.
    /// </summary>
    public class NetworkCommandBridge : NetworkBehaviour
    {
        /// <summary>Server-authoritative flag controlling whether cheat-protected commands are allowed.</summary>
        public readonly NetworkVariable<bool> CheatsEnabled = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        readonly Dictionary<string, CommandDomain> _domains = new();
        readonly HashSet<string> _cheatProtected = new();

        /// <summary>Scans command attributes and installs the pre-execute filter.</summary>
        public override void OnNetworkSpawn()
        {
            ScanCommandAttributes();
            RegisterBuiltInCommands();
            CommandRegistry.Instance.PreExecuteFilter = FilterCommand;
        }

        /// <summary>Removes the pre-execute filter, reverting the console to local-only mode.</summary>
        public override void OnNetworkDespawn()
        {
            CommandRegistry.Instance.PreExecuteFilter = null;
        }

        void ScanCommandAttributes()
        {
            foreach (var kvp in CommandRegistry.Instance.Commands)
            {
                var cmd = kvp.Value;
                if (cmd.Method == null) continue;

                var domainAttr = cmd.Method.GetCustomAttribute<CommandDomainAttribute>();
                if (domainAttr != null)
                    _domains[cmd.Name] = domainAttr.Domain;

                if (cmd.Method.GetCustomAttribute<CheatProtectedAttribute>() != null)
                    _cheatProtected.Add(cmd.Name);
            }
        }

        void RegisterBuiltInCommands()
        {
            CommandRegistry.Instance.Register("sv_cheats", args =>
            {
                if (args.Length == 0)
                    return $"sv_cheats = {(CheatsEnabled.Value ? "1" : "0")}";

                if (!NetworkManager.IsServer)
                    return null; // filter will intercept and send to server

                CheatsEnabled.Value = args[0] == "1";
                return $"sv_cheats set to {(CheatsEnabled.Value ? "1" : "0")}";
            }, "Enable/disable cheat commands", "Server");

            _domains["sv_cheats"] = CommandDomain.Server;
        }

        CommandRegistry.ExecutionResult? FilterCommand(RegisteredCommand cmd, string[] args)
        {
            // Check cheat protection
            if (_cheatProtected.Contains(cmd.Name) && !CheatsEnabled.Value)
                return CommandRegistry.ExecutionResult.Error(
                    $"'{cmd.Name}' requires sv_cheats 1.");

            // Resolve domain (default = Shared)
            if (!_domains.TryGetValue(cmd.Name, out var domain))
                domain = CommandDomain.Shared;

            // Client and Shared commands always execute locally
            if (domain is CommandDomain.Client or CommandDomain.Shared)
                return null;

            // Server commands: host executes locally, clients send RPC
            if (domain == CommandDomain.Server)
            {
                if (NetworkManager.IsServer)
                    return null;

                // Reconstruct raw input for RPC
                var rawInput = cmd.Name;
                if (args.Length > 0)
                    rawInput += " " + string.Join(" ", args);

                ExecuteOnServerRpc(rawInput);
                return CommandRegistry.ExecutionResult.Ok("Sent to server.");
            }

            return null;
        }

        [Rpc(SendTo.Server)]
        void ExecuteOnServerRpc(string rawInput, RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;

            // Temporarily remove filter to avoid recursion
            var filter = CommandRegistry.Instance.PreExecuteFilter;
            CommandRegistry.Instance.PreExecuteFilter = null;

            var result = CommandRegistry.Instance.Execute(rawInput);

            CommandRegistry.Instance.PreExecuteFilter = filter;

            // Send result back to the requesting client
            var message = result.Message ?? "";
            SendCommandResultRpc(message, result.Success,
                RpcTarget.Single(senderId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void SendCommandResultRpc(string message, bool success, RpcParams rpcParams = default)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (success)
                ConsoleLog.Log(message);
            else
                ConsoleLog.LogError(message);
        }
    }
}
