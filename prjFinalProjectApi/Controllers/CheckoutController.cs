using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Data;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckoutController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        public CheckoutController(DbNursingHomeContext context) => _context = context;

        // POST: api/Checkout
        // 建立訂單（會員下單）
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.OrderDetails == null || dto.OrderDetails.Count == 0)
                return BadRequest("訂單明細不可為空");

            // 1) 計算商品小計（以後端重算為準）
            int itemsTotal = 0;
            var details = new List<ShopOrderDetail>();

            foreach (var d in dto.OrderDetails)
            {
                var sub = d.UnitPrice * d.Quantity;
                if (sub < 0) sub = 0;

                itemsTotal += sub;

                details.Add(new ShopOrderDetail
                {
                    ProductId = d.ProductId,
                    ProductName = d.ProductName,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Subtotal = sub,
                    Discount = 0
                });
            }

            // 2) 以「前端傳來的 totalAmount（小計+運費）」為主，做防呆
            //    - 若小於 itemsTotal，則回退為 itemsTotal（避免被惡意降低）
            var total = dto.TotalAmount;
            if (total < itemsTotal) total = itemsTotal;

            // 3) 交易：產生訂單編號、寫入主檔＋明細
            using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var orderNo = await GenerateOrderNoAsync();

            var order = new ShopOrder
            {
                FMemberId = dto.MemberId,
                BuyerName = dto.BuyerName,
                ReceiverName = dto.ReceiverName,
                ReceiverPhone = dto.ReceiverPhone,
                PaymentMethod = dto.PaymentMethod,
                DeliveryMethod = dto.DeliveryMethod,
                DeliveryAddress = dto.DeliveryAddress,
                InvoiceTitle = dto.InvoiceTitle,
                InvoiceTax = dto.InvoiceTax,
                InvoiceInMethod = dto.InvoiceType,   // 發票方式
                CarrierNumber = dto.CarrierNumber,
                OrderTime = DateTime.Now,
                TotalAmount = total,                 // 寫入「小計 + 運費」
                Note = dto.Note,
                Status = "未付款",
                OrderNo = orderNo
            };

            _context.ShopOrders.Add(order);
            await _context.SaveChangesAsync();

            foreach (var de in details)
            {
                de.OrderId = order.OrderId;
                _context.ShopOrderDetails.Add(de);
            }
            await _context.SaveChangesAsync();

            await tx.CommitAsync();

            // （可選）計算回傳用 shippingFee，方便前端除錯顯示
            var shippingFee = Math.Max(0, total - itemsTotal);

            return Ok(new
            {
                success = true,
                orderNo,
                orderId = order.OrderId,
                itemsTotal,
                shippingFee,
                totalAmount = total, // = itemsTotal + shippingFee
                merchantTradeNo = orderNo
            });
        }

        // 併發安全的當日流水號：ORD-YYYYMMDD-0001
        private async Task<string> GenerateOrderNoAsync()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            string prefix = $"ORD-{today}-";

            var last = await _context.ShopOrders
                .Where(o => o.OrderNo != null && o.OrderNo.StartsWith(prefix))
                .OrderByDescending(o => o.OrderNo)
                .Select(o => o.OrderNo)
                .FirstOrDefaultAsync();

            int next = 1;
            if (!string.IsNullOrEmpty(last))
            {
                var tail = last.Split('-').Last(); // "0007"
                if (int.TryParse(tail, out var n)) next = n + 1;
            }
            return prefix + next.ToString("D4");
        }
    }
}
