namespace prjFinalProjectApi.Models.Dto
{
    public class CommunityProfileDto
    {
        public int MemberId { get; set; }
        public string? Name { get; set; }
        public string? PhotoUrl { get; set; }
        public string? Bio { get; set; }
        public int Followers { get; set; }
        public bool IsFollowing { get; set; }
    }
}
