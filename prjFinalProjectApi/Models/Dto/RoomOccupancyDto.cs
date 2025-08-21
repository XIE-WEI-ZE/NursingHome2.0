using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models.Dto
{
    public class RoomOccupancyDto
    {
        public int FOccupancyId { get; set; } // 可選，由後端生成
        [Required]
        public int FBedId { get; set; } // 對應 RoomOccupancy 的 FBedId
        [Required]
        public DateTime FCheckInDate { get; set; } // 移除 null 可能性
        [Required]
        public int FBillingAmount { get; set; } 
        [Required]
        [MaxLength(50)]
        public string FPaymentMethod { get; set; } = null!;
        // 可選添加其他屬性，如客戶信息，若需要
        // [MaxLength(100)]
        // public string Name { get; set; } = null!;
        // [EmailAddress]
        // [MaxLength(255)]
        // public string Email { get; set; } = null!;
        // [MaxLength(20)]
        // public string Contact { get; set; } = null!;
    }
}