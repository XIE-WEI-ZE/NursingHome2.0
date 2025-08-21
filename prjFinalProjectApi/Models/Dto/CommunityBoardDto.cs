namespace prjFinalProjectApi.Models.Dto
{
    public class CommunityBoardDto
    {
        public string BoardName { get; set; }
        public string BoardDescription { get; set; }
        public string BoardStatus { get; set; }
        public int ModeratorId { get; set; }
        public IFormFile? BoardImage { get; set; }
    }
}
