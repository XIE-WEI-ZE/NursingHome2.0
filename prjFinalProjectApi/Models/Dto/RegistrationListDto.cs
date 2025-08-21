namespace prjFinalProjectApi.Models.Dto
{
    public class RegistrationListDto
    {
        public int RegistrationId { get; set; }
        public string RegistrationNum { get; set; }
        public int EventBatchId { get; set; }
        public int MemberId { get; set; }
        public decimal? AmountDue { get; set; }
        public DateTime RegistrationDateTime { get; set; }
        public int CurrentStatus { get; set; }
        public string InternalRemarks { get; set; }
        public string EventName { get; set; }
        public DateTime EventDateTimeStart { get; set; }//活動時間
        public string EventLocation { get; set; } = null!;//活動地點
    }
}
