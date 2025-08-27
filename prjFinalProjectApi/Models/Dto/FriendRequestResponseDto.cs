namespace prjFinalProjectApi.Models.Dto
{
    public class FriendRequestResponseDto
    {
        public int RequestID { get; set; }
        public string Action { get; set; } // "Accepted" 或 "Rejected"
    }
}
