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
        [MaxLength(255)] // 可選，儲存 PayPal 訂單 ID
        public string FPaypalOrderId { get; set; } // 新增 PayPal 訂單 ID
    }
}