using System;

namespace prjFinalProjectApi.Models.Dto
{
    /// <summary>假別清單 DTO</summary>
    public class EmployeeLeaveTypeDto
    {
        public int LeaveTypeId { get; set; }
        public string TypeName { get; set; } = "";
    }

    /// <summary>請假單建立用 DTO（前端送入）</summary>
    // 後端
    // Models/Dto/EmployeeLeaveDto.cs 或你放 DTO 的檔案
    // Models/Dto/EmployeeLeaveCreateDto.cs
   
    public class EmployeeLeaveCreateDto
    {
        public int LeaveTypeId { get; set; }
        public DateTime StartDate { get; set; }   // yyyy-MM-dd
        public DateTime EndDate { get; set; }     // yyyy-MM-dd
        public string StartTime { get; set; } = default!; // "HH:mm"
        public string EndTime { get; set; } = default!;
        public string? Reason { get; set; }

        // ★ 新增：第一關代理人（必填，可為自己），但不可是主管
        public int AgentEmployeeId { get; set; }
    }


    /// <summary>請假單更新用 DTO（主管審核/修改原因等）</summary>
    public class EmployeeLeaveUpdateDto
    {
        public string? Reason { get; set; }
        public string? Status { get; set; } // Pending / Approved / Rejected / Cancelled
    }

    /// <summary>請假紀錄回傳 DTO（查詢列表/單筆明細）</summary>
    public class EmployeeLeaveDto
    {
        public int LeaveId { get; set; }
        public int EmployeeId { get; set; }
        public int LeaveTypeId { get; set; }
        public string LeaveTypeName { get; set; } = "";

        // 由 DateOnly? 轉 DateTime 回傳
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // 從 Reason 標記解析
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }

        public decimal LeaveHours { get; set; }
        public string Status { get; set; } = "";

        public DateTime? ApplyDate { get; set; }
        public DateTime? ApprovedDate { get; set; }

        public string? Reason { get; set; }
        public string? ApproverName { get; set; }
        public string DepartmentName { get; set; } = ""; 
        public string JobTitleName { get; set; } = "";

    }

    /// <summary>員工端列表顯示用（精簡）</summary>
    public class EmployeeLeaveListDto
    {
        public int LeaveId { get; set; }
        public string LeaveTypeName { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal LeaveHours { get; set; }
        public string Status { get; set; } = "";
        public DateTime? ApplyDate { get; set; }
    }

    /// <summary>前端顯示申請人姓名用</summary>
    public class EmployeeLeaveMeDto
    {
        public string ApplicantName { get; set; } = "";
    }

    /// <summary>同部門代理人清單（帶職稱）</summary>
    public class EmployeeAgentDto
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; } = "";
        public int JobTitleId { get; set; }             // 以 0 表示沒有職稱
        public string JobTitleName { get; set; } = "";
        public string Display { get; set; } = "";        // "職稱-姓名" 組好的顯示字串
    }
}
