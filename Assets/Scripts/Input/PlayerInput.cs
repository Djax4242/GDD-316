using UnityEngine;

public class PlayerInput : MonoBehaviour
{
    /// <summary>
    ///
    ///     Handles the players input
    /// 
    /// </summary>
    
    
    
    private PlayerInputActionsMap _playerInputActionsMap;
    
    
    
    private void Awake()
    {
        _playerInputActionsMap = new PlayerInputActionsMap();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnEnable() =>_playerInputActionsMap.Enable();
    private void OnDisable() => _playerInputActionsMap.Disable();

    public Vector2 GetMoveInput() => _playerInputActionsMap.Player.Move.ReadValue<Vector2>();
    public bool GetJumpInput() => _playerInputActionsMap.Player.Jump.IsPressed();
    public bool GetSprintInput() => _playerInputActionsMap.Player.Sprint.IsPressed();
}
