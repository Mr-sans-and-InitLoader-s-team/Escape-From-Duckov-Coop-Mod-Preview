using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public sealed class VehicleMovementAnimatorDriver : MonoBehaviour
{
    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int MoveDirXHash = Animator.StringToHash("MoveDirX");
    private static readonly int MoveDirYHash = Animator.StringToHash("MoveDirY");
    private static readonly int VehicleTypeHash = Animator.StringToHash("VehicleType");

    private const float MinMovingDistance = 0.0005f;
    private const float MinAnimSpeedWhenMoving = 0.35f;
    private const float MoveStateHoldTime = 0.2f;

    private CharacterMainControl _vehicle;
    private Animator _animator;
    private Vector3 _lastPosition;
    private bool _hasLastPosition;
    private int _vehicleType;
    private bool _movingState;
    private float _lastMoveDetectedTime;

    public void Bind(CharacterMainControl vehicle, int vehicleType)
    {
        var resolvedType = vehicleType > 0
            ? vehicleType
            : (vehicle != null && vehicle.vehicleAnimationType > 0 ? vehicle.vehicleAnimationType : 1);

        var sameVehicle = _vehicle == vehicle;
        _vehicle = vehicle;
        _vehicleType = resolvedType;

        if (!sameVehicle)
        {
            _animator = ResolveVehicleAnimator(_vehicle);
            _hasLastPosition = false;
            _lastPosition = _vehicle ? _vehicle.transform.position : Vector3.zero;
        }
        else if (_animator == null)
        {
            _animator = ResolveVehicleAnimator(_vehicle);
        }

        if (!sameVehicle)
        {
            _movingState = false;
            _lastMoveDetectedTime = 0f;
        }

        enabled = _vehicle != null;
    }

    private void Update()
    {
        TickAnimator(Time.unscaledDeltaTime);
    }

    private void TickAnimator(float deltaTime)
    {
        if (_vehicle == null)
        {
            enabled = false;
            return;
        }

        if (_animator == null)
            _animator = ResolveVehicleAnimator(_vehicle);
        if (_animator == null)
            return;

        var position = GetVehiclePosition(_vehicle);
        if (!_hasLastPosition)
        {
            _lastPosition = position;
            _hasLastPosition = true;
            return;
        }

        var delta = position - _lastPosition;
        var distance = delta.magnitude;
        _lastPosition = position;

        var movingNow = distance > MinMovingDistance;
        if (movingNow)
            _lastMoveDetectedTime = Time.unscaledTime;

        if (_movingState)
        {
            if (!movingNow && Time.unscaledTime - _lastMoveDetectedTime >= MoveStateHoldTime)
                _movingState = false;
        }
        else if (movingNow)
        {
            _movingState = true;
        }

        var speed = 0f;
        if (_movingState)
        {
            var dt = Mathf.Max(deltaTime, 0.0001f);
            speed = Mathf.Clamp(Mathf.Max(distance / dt, MinAnimSpeedWhenMoving), 0f, 6f);
        }

        TrySetFloat(_animator, MoveSpeedHash, _movingState ? speed : 0f);
        TrySetFloat(_animator, MoveDirXHash, 0f);
        TrySetFloat(_animator, MoveDirYHash, _movingState ? 1f : 0f);
        TrySetInt(_animator, VehicleTypeHash, _vehicleType);
    }

    private static Vector3 GetVehiclePosition(CharacterMainControl vehicle)
    {
        if (!vehicle)
            return Vector3.zero;

        return vehicle.characterModel ? vehicle.characterModel.transform.position : vehicle.transform.position;
    }

    private static Animator ResolveVehicleAnimator(CharacterMainControl vehicle)
    {
        if (!vehicle)
            return null;

        var model = vehicle.characterModel;
        if (model)
        {
            var basic = model.GetComponent<CharacterAnimationControl>();
            if (basic != null && basic.animator != null)
                return basic.animator;

            var magic = model.GetComponent<CharacterAnimationControl_MagicBlend>();
            if (magic != null && magic.animator != null)
                return magic.animator;

            var modelAnim = model.GetComponentInChildren<Animator>(true);
            if (modelAnim)
                return modelAnim;
        }

        return vehicle.GetComponentInChildren<Animator>(true);
    }

    private static void TrySetFloat(Animator animator, int hash, float value)
    {
        if (!animator) return;
        var parameters = animator.parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == hash && parameters[i].type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(hash, value);
                return;
            }
        }
    }

    private static void TrySetInt(Animator animator, int hash, int value)
    {
        if (!animator) return;
        var parameters = animator.parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == hash && parameters[i].type == AnimatorControllerParameterType.Int)
            {
                animator.SetInteger(hash, value);
                return;
            }
        }
    }
}
