namespace prjFinalProjectApi.Models.Dto
{
    public class CreatePostDto
    {
        public int MemberID { get; set; }
        public int BoardID { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int? QuotePostID { get; set; }
        public int? ParentPostID { get; set; }
        public List<IFormFile>? Attachments { get; set; }
    }
}
