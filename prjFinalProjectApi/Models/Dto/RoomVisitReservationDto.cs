using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models.Dto
{
    public class RoomVisitReservationDto
    { // 此為前端預約參訪 room-list.component.ts 的預約參訪表單
        public int FReservationId { get; set; } // 可選，由後端生成

        [Required]
        [MaxLength(100)]
        public string FName { get; set; } = null!;

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string FEmail { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string FPhoneOrLineId { get; set; } = null!;

        [Required]
        public DateTime FReservationDate { get; set; }
    }
    public class BatchUpdateContactDto
    {
        public List<int> ReservationIds { get; set; }
        public bool NewStatus { get; set; } // true: 已聯絡, false: 未聯絡
    }

    public class BatchDeleteDto
    {
        public List<int> ReservationIds { get; set; }
    }
    public class BatchUpdateDateDto
    {
        public List<int> ReservationIds { get; set; }
        public DateTime NewDate { get; set; }
    }
}