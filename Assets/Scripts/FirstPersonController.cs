using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public Transform cameraPivot;
    public float walkSpeed = 1.4f;
    public float sprintSpeed = 4f;
    public float lookSensitivity = 0.1f;
    public float jumpHeight = 0.5f;
    public float gravity = -9.81f;

    CharacterController controller;
    InputAction move, look, jump, sprint;
    float pitch, verticalVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        move = InputSystem.actions.FindAction("Move");
        look = InputSystem.actions.FindAction("Look");
        jump = InputSystem.actions.FindAction("Jump");
        sprint = InputSystem.actions.FindAction("Sprint");
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        Vector2 l = look.ReadValue<Vector2>() * lookSensitivity;
        transform.Rotate(0f, l.x, 0f);
        pitch = Mathf.Clamp(pitch - l.y, -85f, 85f);
        cameraPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        if (controller.isGrounded && jump.WasPressedThisFrame())
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        verticalVelocity += gravity * Time.deltaTime;

        Vector2 m = move.ReadValue<Vector2>();
        float speed = sprint.IsPressed() ? sprintSpeed : walkSpeed;
        Vector3 velocity = (transform.right * m.x + transform.forward * m.y) * speed;
        velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }
}