using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models.Dto
{
    public class UpdateRoomERPDto
    {
        [Required]
        public int FRoomId { get; set; }
        [Required]
        public string FRoomAlias { get; set; } = null!;
        [Required]
        public string FRoomDescription { get; set; } = null!;
        [Required]
        public int FRoomPrice { get; set; }
        [Required]
        public int FBedCount { get; set; }
        public bool FRoomType { get; set; } // true: 單人房, false: 多人房
        public string FRoomStatus { get; set; } = "active"; // 'active' | 'inactive'
        public IFormFile? RoomImage { get; set; } // 可選，圖片上傳
    }
}