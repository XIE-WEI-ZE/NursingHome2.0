namespace prjFinalProjectApi.Models.Dto.ERP
{
    public class UpdateMemberERPDto
    {
        public string? FName { get; set; }
        public string? FEmail { get; set; }
        public string? FGender { get; set; }
        public string? FPhone { get; set; }
        public string? FCity { get; set; }
        public string? FDistrict { get; set; }
        public string? FRoadAddress { get; set; }
        public string? FBirthDate { get; set; }  
        public bool? FAccountStatus { get; set; }
        public string? FProfilePictureUrl { get; set; }
        public bool? FResidesInCareHomeStatus { get; set; }
    }
}
