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
    // Cuerpo de una persona adulta: 0,48 m de ancho con la piel (radio 0,22 + 0,02) y 1,8 m de alto. Con 0,3 de radio
    // y 0,08 de piel no cabía por puertas de 0,7 m (cabinas de aseo, cuartos pequeños)
    public float radio = 0.22f;
    public float altura = 1.8f;
    public float piel = 0.02f;

    CharacterController controller;
    InputAction move, look, jump, sprint;
    float pitch, verticalVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        controller.radius = radio;
        controller.height = altura;
        controller.center = new Vector3(0f, altura / 2f, 0f);
        controller.skinWidth = piel;
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