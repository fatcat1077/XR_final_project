using UnityEngine;

public enum UserRole
{
    None,
    Teacher,
    Student
}

public static class LocalUserProfile
{
    public static UserRole Role = UserRole.None;
    public static string RoomName = "XRRoom01";
}