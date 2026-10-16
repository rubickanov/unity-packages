namespace Rubickanov.Characters
{
    /// <summary>Desired object pose drives the torso first; final reachable grips are prepared before hand IK.</summary>
    public interface IWorkHandPose
    {
        UnityEngine.Vector3? PrepareBody();
        void PrepareHands();
    }
}
