



using Newtonsoft.Json;
using static ZapperReponse.DataZp;

public class ZapperReponse
{
    public DataZp data { get; set; }
    public class DataZp
    {
        public List<TotalZp> totals { get; set; }
        public class TotalZp
        {
            public string? balanceUSD { get; set; }
        }
    }


    public static double AddressBalance(string formattedResponse)
    {
        List<ZapperReponse> responseList = JsonConvert.DeserializeObject<List<ZapperReponse>>(
            formattedResponse
        );

        double sum = 0;

        for (int i = 0; i < responseList.Count(); i++)
        {
            List<TotalZp> listTotals = responseList.ElementAt(i).data.totals;

            for (int j = 0; j < listTotals.Count(); j++)
            {
                TotalZp currKey = listTotals.ElementAt(j);

                double tryParse = 0;
                double.TryParse(currKey.balanceUSD, out tryParse);

                sum += (tryParse);
            }
        }

        Console.WriteLine("####" + sum);

        return 1;
    }
}