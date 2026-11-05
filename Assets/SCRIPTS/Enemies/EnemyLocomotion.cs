using UnityEngine;

/// <summary>
/// How an enemy moves on the server: <see cref="EnemyMotor"/> walks on the NavMesh, <see cref="EnemyFlightMotor"/>
/// flies freely in 3D. <see cref="EnemyBrain"/> only needs this shared surface to animate and face its body.
/// </summary>
public abstract class EnemyLocomotion : MonoBehaviour
{
    /// <summary>Prepares movement (e.g. snapping onto the NavMesh). Returns false while the enemy cannot move yet.</summary>
    public abstract bool Activate();
    public abstract Vector3 Velocity { get; }
    public abstract bool IsMoving { get; }
    public abstract void Stop();
    public abstract void Warp(Vector3 position);

    /// <summary>Turns instantly (no smoothing) to look horizontally at <paramref name="point"/>.</summary>
    public void FaceTowards(Vector3 point) => FaceDirection(point - transform.position);

    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    public void FaceYaw(float yaw) => transform.rotation = Quaternion.Euler(0f, yaw, 0f);
}
