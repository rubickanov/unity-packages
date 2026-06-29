using System.Collections.Generic;

namespace Rubickanov.Motor.Tests
{
    /// <summary>
    /// Minimal <see cref="IMotorModule"/> that records its lifecycle calls so
    /// <see cref="MotorSimulation"/> orchestration can be asserted.
    /// When multiple instances share a <c>log</c>, call order across modules
    /// is captured in a single sequence.
    /// </summary>
    internal sealed class RecordingModule : IMotorModule
    {
        private readonly int _priority;
        private readonly string _name;

        public int Priority => _priority;
        public bool IsActive { get; set; } = true;

        public readonly List<string> Log;
        public int InitializeCalls;
        public int SimulateCalls;
        public int VisualUpdateCalls;
        public float LastDeltaTime;
        public MotorState? State;
        public IMotorBody? Body;
        public IModuleResolver? Resolver;

        public RecordingModule(int priority, List<string>? log = null, string? name = null)
        {
            _priority = priority;
            _name = name ?? priority.ToString();
            Log = log ?? new List<string>();
        }

        public void Initialize(MotorState state, IMotorBody body, IModuleResolver resolver)
        {
            InitializeCalls++;
            State = state;
            Body = body;
            Resolver = resolver;
            Log.Add($"init:{_name}");
        }

        public void Simulate(float deltaTime)
        {
            SimulateCalls++;
            LastDeltaTime = deltaTime;
            Log.Add($"sim:{_name}");
        }

        public void VisualUpdate(float deltaTime)
        {
            VisualUpdateCalls++;
            LastDeltaTime = deltaTime;
            Log.Add($"vis:{_name}");
        }
    }
}
