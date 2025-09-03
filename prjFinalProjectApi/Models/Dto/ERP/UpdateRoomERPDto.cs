using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models.Dto
{
    public class UpdateRoomERPDto
    {
        [Required]
        public int FRoomId { get; set; }
        [Required]
        public string FRoomName { get; set; } = null!;
        [Required]
        public string FRoomAlias { get; set; } = null!;
        [Required]
        public string FRoomDescription { get; set; } = null!;
        [Required]
        public int FRoomPrice { get; set; }
        [Required]
        public int FBedCount { get; set; }
        public bool FRoomType { get; set; } // false: 單人房, true: 多人房
        [Required]
        [RegularExpression("^(active|vacant)$", ErrorMessage = "狀態必須為 'active' 或 'vacant'")]
        public string FRoomStatus { get; set; } = "active";
        public IFormFile[]? RoomImages { get; set; } // 支援多張圖片
        public string[]? ExistingImages { get; set; } // 現有圖片路徑
    }
}