using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace SallaWinFormsDemo
{
    public partial class Form1 : Form
    {
        // عدّل اتصالك
        private readonly string _connStr =
            "Server=.;Database=AswaqTestDCV3;Trusted_Connection=True;TrustServerCertificate=True;";
        //comment
        /* ضع بيانات تطبيق سِلّة
         OAuth Mode
            Easy Mode
            The Access Token (and Refresh Token) can be retrieved using the (app.store.authorize) webhook event once the merchant installs the App.
             Custom Mode
            A custom web page needs to be created to handle the callback URLs and retrieve the Access Token and Refresh Token.
         */
        private readonly string _clientId = "4fa28b07-97a6-4d63-bfb3-c459d857f7d2";//"YOUR_CLIENT_ID";
        private readonly string _clientSecret = "70cdb54db6c72310cb2ea074198adb76ffdc0240b727e6a02243af449119fc65";//"YOUR_CLIENT_SECRET";
        private readonly string _refreshToken = "0f53a8e119a119ef89e8b852619c0789bed18c27e80adcbb90603d7e7ecac857";//"YOUR_REFRESH_TOKEN";

        public Form1()
        {
            InitializeComponent();
        }

        private async void btnImport_Click(object sender, EventArgs e)
        {
            try
            {
                txtLog.AppendText("Authenticating...\r\n");

                var api = new SallaApi();
                var accessToken = await api.RefreshTokenAsync(_clientId, _clientSecret, _refreshToken);

                txtLog.AppendText("Access token OK.\r\n");

                // مثال: مزامنة من آخر 3 أيام (عدّلها حسب لوجيك المزامنة لديك)
                DateTime? createdFromUtc = DateTime.UtcNow.AddDays(-3);

                int page = 1;
                while (true)
                {
                    var ordersPage = await api.GetOrdersPageAsync(accessToken, page, createdFromUtc);

                    var data = (JArray)ordersPage["data"];
                    if (data == null || data.Count == 0)
                    {
                        txtLog.AppendText("No more orders.\r\n");
                        break;
                    }

                    foreach (var ord in data)
                    {
                        // --- قراءات أساسية من سِلّة ---
                        long extId = ord["id"] != null ? (long)ord["id"] : 0;
                        string number = ord["number"] != null ? (string)ord["number"] : null;
                        DateTime created = ord["created_at"] != null ? DateTime.Parse((string)ord["created_at"]) : DateTime.UtcNow;

                        decimal total = ord["total"] != null ? (decimal)ord["total"] : 0m; // غالبًا قبل الضريبة
                        decimal tax = ord["tax"] != null ? (decimal)ord["tax"] : 0m;
                        decimal discount = ord["discount"] != null ? (decimal)ord["discount"] : 0m;
                        decimal net = total + tax - discount; // عدّل المعادلة لو API سِلّة يعطي Net صريح

                        txtLog.AppendText($"Order #{number} ({extId}) @ {created:u}\r\n");

                        // --- عناصر الطلب ---
                        var itemsObj = await api.GetOrderItemsAsync(accessToken, extId);
                        var items = (JArray)itemsObj["data"];
                        if (items == null || items.Count == 0)
                        {
                            txtLog.AppendText("   No items. Skipped.\r\n");
                            continue;
                        }

                        // أول بند للهيدر (السطر 0001)
                        var first = items[0];
                        string firstSku = first["sku"] != null ? (string)first["sku"] : null;
                        // لو SKU ≠ كود الصنف عندك… اعمل Lookup/Mapping هنا:
                        string firstItemCode = MapSkuToLocalCode(firstSku);

                        double firstQty = first["quantity"] != null ? (double)first["quantity"] : 0.0;
                        double firstPrice = first["price"] != null ? (double)first["price"] : 0.0;

                        // إجماليات للهيدر
                        double totalD = (double)total;
                        double taxD = (double)tax;
                        double discD = (double)discount;
                        double netD = (double)net;
                        double grossD = (double)total; // قبل الضريبة

                        // 1) إدخال الهيدر + التقاط رقم الأمر
                        var invoiceNum = await InsertHeaderAndGetInvoiceAsync(
                            totalD, netD, taxD, grossD, firstQty, firstPrice, firstItemCode);

                        txtLog.AppendText("   Header inserted: " + invoiceNum + "\r\n");

                        // 2) إدخال باقي البنود
                        int lineNo = 2;
                        for (int i = 1; i < items.Count; i++)
                        {
                            var it = items[i];

                            string sku = it["sku"] != null ? (string)it["sku"] : null;
                            string code = MapSkuToLocalCode(sku);

                            double qty = it["quantity"] != null ? (double)it["quantity"] : 0.0;
                            double price = it["price"] != null ? (double)it["price"] : 0.0;

                            await InsertDetailAsync(
                                cComment: "Imported from Salla",
                                fItmCode: code,
                                qty: qty,
                                itmPrice: price,
                                databaseName: "2025", // عدّل حسب ثوابتك
                                cYear: "2025",
                                itLineNo: lineNo.ToString("0000"),
                                invoiceNum: invoiceNum
                            );

                            txtLog.AppendText("   Detail line " + lineNo.ToString("0000") + " inserted\r\n");
                            lineNo++;
                        }
                    }

                    // هل في صفحة تالية؟
                    bool hasNext = false;
                    var links = ordersPage["links"];
                    if (links != null && links["next"] != null && links["next"].Type != JTokenType.Null)
                        hasNext = true;

                    if (!hasNext) break;
                    page++;
                }

                txtLog.AppendText("Import finished.\r\n");
            }
            catch (Exception ex)
            {
                txtLog.AppendText("ERROR: " + ex.Message + "\r\n");
            }
        }

        /// <summary>
        /// هنا بتربط الـ SKU القادم من سِلّة بكود الصنف المحلي عندك
        /// نفّذ Lookup من جدول Mapping أو رجّع نفس الـ SKU لو متطابق
        /// </summary>
        private string MapSkuToLocalCode(string sku)
        {
            // TODO: اعمل استعلام على جدول Mapping لو عندك
            // مؤقتًا نعتبره نفس الكود:
            return string.IsNullOrEmpty(sku) ? "UNKNOWN" : sku;
        }

        private async Task<string> InsertHeaderAndGetInvoiceAsync(
            double total, double net, double tax, double fGross,
            double qty, double itPrice, string itmCode)
        {
            using (var cn = new SqlConnection(_connStr))
            {
                await cn.OpenAsync();

                using (var cmd = new SqlCommand("insertdatatosorder", cn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@Total", total);
                    cmd.Parameters.AddWithValue("@Net", net);
                    cmd.Parameters.AddWithValue("@Tax", tax);
                    cmd.Parameters.AddWithValue("@fGross", fGross);

                    // أول بند يتسجّل داخل الهيدر كسطر 0001
                    cmd.Parameters.AddWithValue("@Qty", qty);
                    cmd.Parameters.AddWithValue("@ItPrice", itPrice);
                    cmd.Parameters.AddWithValue("@itmCode", itmCode);

                    var pOut = cmd.Parameters.Add("@OutInvoiceNum", SqlDbType.Char, 12);
                    pOut.Direction = ParameterDirection.Output;

                    await cmd.ExecuteNonQueryAsync();
                    return (pOut.Value ?? "").ToString().Trim();
                }
            }
        }

        private async Task InsertDetailAsync(
            string cComment, string fItmCode, double qty, double itmPrice,
            string databaseName, string cYear, string itLineNo, string invoiceNum)
        {
            using (var cn = new SqlConnection(_connStr))
            {
                await cn.OpenAsync();

                using (var d = new SqlCommand("InsertDatatoSorderDetials", cn))
                {
                    d.CommandType = CommandType.StoredProcedure;

                    d.Parameters.AddWithValue("@cComment", cComment);
                    d.Parameters.AddWithValue("@fItmCode", fItmCode);
                    d.Parameters.AddWithValue("@Qty", qty);
                    d.Parameters.AddWithValue("@ItmPrice", itmPrice);
                    d.Parameters.AddWithValue("@databasename", databaseName);
                    d.Parameters.AddWithValue("@cYear", cYear);
                    d.Parameters.AddWithValue("@itLineNo", itLineNo);
                    d.Parameters.AddWithValue("@cInvoicNumD", invoiceNum);

                    await d.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
