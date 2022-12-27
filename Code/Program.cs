using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using CsvHelper;
using Newtonsoft.Json;
using System.Net.Http;
using DotNetEnv;
using System.Diagnostics;
using System.Web.Http.Results;

class Program
{
    public static void Main()
    {
        DotNetEnv.Env.Load();

        bool development = false;
        if (!development)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            WebApplication app = builder.Build();

            app.UseHttpsRedirection();
            app.UseSwagger();
            app.UseSwaggerUI();


            app = Maps.MapPost(app);

            app.Run();
        }

        SmartWallets.Get(File.ReadAllText("here.csv"), 10000, 5, 1000, "", "");
    }
}

class Maps
{
    public static WebApplication MapPost(WebApplication app)
    {
        //?balance=10000&txs=5&minswap=1000&token=<token address>
        app.MapPost("/upload",
            async (HttpRequest request, int? balance, int? txs, int? minswap, string? token, string? buyonly) =>
            {
                string fileContent = "";
                using (var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8))
                {
                    // Read the raw file as a `string`.
                    fileContent = await reader.ReadToEndAsync();
                }

                return SmartWallets.Get(fileContent, balance, txs, minswap, token, buyonly);
            }
        ); //.Accepts<IFormFile>("json"); //NEED TO CLARIFY HOW THIS WORKS

        return app;
    }
}

class SmartWallets
{
    public static string Get(string csv, int? balance, int? numOfTxs, int? minSwap, string token, string buyonly)
    {
        //if value is null then we assign it its default value
        balance = balance ?? 10000; numOfTxs = numOfTxs ?? 5; minSwap = minSwap ?? 1000; token = token ?? "";
        buyonly = buyonly ?? "false";

        //load networks

        //csv to list of TXs
        List<Tx> list = Data.CsvToListOfTXs(csv);
        //filter for duplicates by txhash
        //list = list.GroupBy(el => el.Txhash).Select(el => el.First()).ToList<Tx>();

        /*!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        0. Determine the network of TXs

        1. Swap value > <minswap|1000$>
        If true:
            Check that the wallet is active: 
            1.1 — Balance is > <balance|10000>$ in all chains. 
            1.2 — TX history is > <txs|5> transactions for the last 7 days.
        */

        //ISSUES
        //to determine which network is it, the bot takes first tx hash and searches for the transaction
        //in all of the supported networks.

        //determine the network
        //What if the Api is down!!!

        string network = Explorer.DetermineNetwork(list);
        //if returns "" then throw exception: "couldnt determine network"

        //1. SWAP VALUE > minSwap|1000$
        // check for stables vs 1000
        // need to hardcode:
        // chain native tokens(5) and wrapped(5) and natives in the network, stables(usdt,busd,dai,usdn etc)
        // need to get the prices for native tokens (eth, bnb, matic)
        list = TxValue.FilterTxWithLessThanMinswap(list, network, minSwap);
        //now we need to implement solution for
        //1. buyonly included
        //2. tokenfilter included

        //buyonly section: to filter buyonly txs we need to be sure that in the last action
        // of the trnsaction the sender of the transaction receives That token, which we are working with
        //        
        //!!! So, if buyonly is true, than we also need tokenContract to be specified.
        // Or we can try to guess it, based on the statistics...

        //first we need to find out which token are we working with
        // my plan is to choose a token that is not a stable and is
        // met most frequently
        string tokenContract = Data.DetermineTokenContract(list, network);

        List<SmartWallet> walletsList = new List<SmartWallet>();

        //need to take both "from" and "to" wallets and check them both.
        //if wallet holds more then 50 million, it's probably a dex or smth
        if (buyonly == "true")
        {
            walletsList = Data.FilterBuyOnly(list, tokenContract);
        }
        else
        {
            walletsList = Data.FilterRegular(list, tokenContract); // here tokencontract is used to track the bought amount
        }

        //to track pnl we'd neet to save the timestamp of position opening,
        //probably get the average price of token on the timestamp
        // and the track profit from selling afterwards

        //is there any reason we would need to track sell actions during this phase, because this phase is probably the accumulation
        // so sell actions aren't really of our concern

        //####################################
        // im stuck on the tx actions. when do we add wallet using tx.From and when tx.To
        // i 100% need to know, hwo much did they sell during this snapshot, to know how much they actually bought after all...
        // but i will do this after it's all working as it's optional

        File.WriteAllText("wallets.json", JsonConvert.SerializeObject(walletsList, Formatting.Indented));

        SmartWallets.FilterTxCount(walletsList, numOfTxs);

        //now we filter them according to wallet balance
        // debank has an api to check total usd balance of the address, but its not free. 0.006$ per request
        //100 requests per second
        // tried zapper.fi but it's too slow, 30rpm
        // for now sticked to the debank api, api was stolen from the page load calls
        //tested it and it didnt ban for fraud. yet. :)
        SmartWallets.FilterBalance(walletsList, balance);

        File.WriteAllText("here.json", JsonConvert.SerializeObject(list, Formatting.Indented));
        File.WriteAllText("wallets.json", JsonConvert.SerializeObject(walletsList, Formatting.Indented));

        return JsonConvert.SerializeObject(walletsList);
    }

    private static List<SmartWallet> FilterTxCount(List<SmartWallet> walletsList, int? numOfTxs)
    {
        // using etherscan api
        // get current time in unix format
        // we call #Blocks Get Block Number by Timestamp, where timestamp is
        // a week ago from now
        //
        // then call #Accounts Get a list of 'Normal' Transactions By Address with startblock
        // that we have received from get block func and endblock 99999999 (just leave blank)

        //timestamp a week ago
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7 * 24 * 60 * 60;

        IList<Network> listOfNetworks = Network.AllNetworks();

        using (HttpClient httpClient = new())
        {
            for (int i = 0; i < walletsList.Count(); i++)
            {
                int txCount = 0;

                string address = walletsList.ElementAt(i).walletAddress;

                // sum up txCount for every network until its > numOfTxs, if not delete wallet
                for (int j = 0; j < listOfNetworks.Count(); j++)
                {
                    long blockNoNow = 0;

                    var network = listOfNetworks.ElementAt(j);

                    //get startBlock
                    blockNoNow = GetBlock(httpClient, timestamp, network).Result;

                    //get txs for address from startBlock
                    txCount += GetTxCount(httpClient, blockNoNow, network, numOfTxs, address).Result;

                    if (txCount > numOfTxs)
                        break;
                }

                if (txCount < numOfTxs)
                {
                    walletsList.RemoveAll(x => x.walletAddress == address);
                    i--;
                }
                else
                {
                    // add recentTxs to wallet object
                    walletsList.ElementAt(i).recentTxs = txCount;
                }
            }
        }

        return walletsList;
    }

    private static async Task<int> GetTxCount(HttpClient httpClient, long startBlock,
        Network network, int? numOfTxs, string address)
    {
        // https://api.etherscan.io/api
        //     ?module=account
        //     &action=txlist
        //     &address=0xc5102fE9359FD9a28f877a67E36B0F050d81a3CC
        //     &startblock=0
        //     &endblock=99999999
        //     &page=1
        //     &offset=10
        //     &sort=asc
        //     &apikey=YourApiKeyToken

        int txCount = 0;

        for (int j = 0; j < 3; j++)
        {
            Stopwatch watch = new();
            watch.Start();

            try
            {
                HttpResponseMessage message = await httpClient.GetAsync(network.Api +
                    "?module=account&action=txlist&address=" + address +
                    "&startblock=" + startBlock + "&page=1" +
                    "&offset=" + (numOfTxs + 1) + "&sort=desc" + "&apikey=" + network.Key);

                string readResponse = await message.Content.ReadAsStringAsync();

                TxResponse response = JsonConvert.DeserializeObject<TxResponse>(readResponse);

                txCount = response.result.Count();

                break;
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc);
                Thread.Sleep(500);
            }

            watch.Stop();
            Thread.Sleep(watch.ElapsedMilliseconds > 200 ? 100 : 300);
        }

        return txCount;
    }

    private class TxResponse
    {
        public string status { get; set; }
        public List<Tx1> result { get; set; }
        public class Tx1
        {
            public string blockNumber { get; set; }
        }
    }

    private static async Task<long> GetBlock(HttpClient httpClient, long timestamp, Network network)
    {
        // https://api.etherscan.io/api
        //     ?module=block
        //     &action=getblocknobytime
        //     &timestamp=1578638524
        //     &closest=before
        //     &apikey=YourApiKeyToken

        long blockNo = 0;

        bool requestSuccess = false;

        for (int j = 0; j < 3; j++)
        {
            Stopwatch watch = new();
            watch.Start();

            try
            {
                HttpResponseMessage message = await httpClient.GetAsync(network.Api +
                    "?module=block&action=getblocknobytime&timestamp=" + timestamp +
                    "&closest=before&apikey=" + network.Key);

                string readResponse = await message.Content.ReadAsStringAsync();

                BlockNo response = JsonConvert.DeserializeObject<BlockNo>(readResponse);

                requestSuccess = long.TryParse(response.result, out blockNo);
                if (!requestSuccess)
                    throw new Exception($"couldn't parse blocknumber\n{readResponse}");

                break;
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc);
                Thread.Sleep(500);
            }

            watch.Stop();
            Thread.Sleep(watch.ElapsedMilliseconds > 200 ? 100 : 300);
        }

        return blockNo;
    }

    private class BlockNo
    {
        // {"status":"0","message":"NOTOK","result":"Max rate limit reached"}
        // {"status":"1","message":"OK","result":"9251482"}

        public string status { get; set; }
        public string result { get; set; }
    }

    private static List<SmartWallet> FilterBalance(List<SmartWallet> walletsList, int? balance)
    {
        for (int i = 0; i < walletsList.Count();)
        {
            SmartWallet wallet = walletsList.ElementAt(i);

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            double getBalance = Data.GetBalanceForWallet(wallet).Result;

            stopwatch.Stop();
            Console.Write("ellapsed time" + stopwatch.ElapsedMilliseconds.ToString());
            Thread.Sleep(stopwatch.ElapsedMilliseconds > 200 ? 100 : 300);

            Console.WriteLine(i);

            if (getBalance < balance)
                walletsList.RemoveAt(i);
            else
            {
                walletsList.ElementAt(i).total_balance = getBalance;
                i++;
            }
        }

        return walletsList;
    }
}



class Data
{
    public static List<Tx> CsvToListOfTXs(string text)
    {
        var reader = new StringReader(text);
        var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);

        var records = csv.GetRecords<Tx>().ToList<Tx>();

        for (int i = 0; i < records.Count(); i++)
        {
            string txTokenValue = records.ElementAt(i).TokenValue;
            if (txTokenValue.IndexOf(",") != -1)
                records.ElementAt(i).TokenValue = txTokenValue.Remove(txTokenValue.IndexOf(","), 1);
        }

        return records;
    }


    public static string DetermineTokenContract(List<Tx> list, string networkForStables)
    {
        //this method can also do some statistics on whether the token
        // get chosen the proper way

        string[] stablesContractsInTheNetwork = Stable.ArrayContractsInNetwork(networkForStables);

        IList<TokenContractFilter> listCounter = new List<TokenContractFilter>();

        for (int i = 0; i < list.Count(); i++)
        {
            Tx tx = list.ElementAt(i);

            int indexOfContract = -1;
            //if this contract is already on the list, take its index. otherwise -1
            if (listCounter.Any(token => token.contract.Equals(list.ElementAt(i).ContractAddress,
                    StringComparison.OrdinalIgnoreCase)))
            {
                indexOfContract = listCounter.IndexOf(listCounter.First(x => x.contract.Equals(list.ElementAt(i).ContractAddress,
                StringComparison.OrdinalIgnoreCase)));
            }

            if (indexOfContract != -1)
            {
                listCounter.ElementAt(indexOfContract).counter++;
            }
            else if (!stablesContractsInTheNetwork.Any(list.ElementAt(i).ContractAddress.ToLower().Contains))
            {
                listCounter.Add(new TokenContractFilter()
                {
                    name = tx.TokenName,
                    contract = tx.ContractAddress,
                    counter = 1
                });
            }
        }

        string mostRepeatedToken_Contract = "";
        for (int i = 0; i < listCounter.Count(); i++)
        {
            if (!listCounter.Any(token => token.counter > listCounter.ElementAt(i).counter))
            {
                mostRepeatedToken_Contract = listCounter.ElementAt(i).contract;
            }
        }

        return mostRepeatedToken_Contract;
    }

    private class TokenContractFilter
    {
        public string name { get; set; }
        public string contract { get; set; }
        public int counter { get; set; }
    }

    public static List<SmartWallet> FilterBuyOnly(List<Tx> list, string tokenContract)
    {
        List<SmartWallet> walletsList = new List<SmartWallet>();

        for (int i = 0; i < list.Count(); i++)
        {
            Tx tx = list.ElementAt(i);

            if (i == list.Count() - 1) //if it's the last tx. => list.ElementAt(i+1) would throw an exception
            {
                CheckWalletBO(tx, walletsList, tokenContract);

                continue;
            }

            if (list.ElementAt(i + 1).Txhash != tx.Txhash)
            {
                //if it is a "buy the specified token" tx, than it's last action would be
                // to transfer the specified token to contract initiator address(buyer)
                //SO, we can save the address of the receiver as the smart wallet address

                CheckWalletBO(tx, walletsList, tokenContract);
            }
        }

        return walletsList; // should work ################################################################
    }

    // check wallet if buyonly | CheckWalletBuyOnly
    private static List<SmartWallet> CheckWalletBO(Tx tx, List<SmartWallet> walletsList, string tokenContract)
    {
        if (tx.ContractAddress == tokenContract)
        {
            SmartWallet wallet = walletsList.FirstOrDefault(x => x.walletAddress == tx.To);

            if (wallet != null)
            {
                // bought
                walletsList.First(x => x.walletAddress == tx.To).bought += double.Parse(tx.TokenValue);
                walletsList.First(x => x.walletAddress == tx.To).txs++;
            }
            else
            {
                walletsList.Add(new SmartWallet(tx.To, double.Parse(tx.TokenValue), 0, "0", "0", 1));
            }
        }

        return walletsList;
    }

    public static List<SmartWallet> FilterRegular(List<Tx> list, string tokenContract)
    {
        List<SmartWallet> walletsList = new List<SmartWallet>();

        for (int i = 0; i < list.Count(); i++)
        {
            Tx tx = list.ElementAt(i);

            if (i == list.Count() - 1) //if it's the last tx. => list.ElementAt(i+1) would throw an exception
            {
                CheckWalletR(tx, walletsList, tokenContract);

                continue;
            }

            if (list.ElementAt(i + 1).Txhash != tx.Txhash)
            {
                //if it is a "buy the specified token" tx, than it's last action would be
                // to transfer the specified token to contract initiator address(buyer)
                //SO, we can save the address of the receiver as the smart wallet address

                CheckWalletR(tx, walletsList, tokenContract);
            }
        }

        return walletsList;
    }

    // check wallet if !buyonly | CheckWalletRegular
    private static List<SmartWallet> CheckWalletR(Tx tx, List<SmartWallet> walletsList, string tokenContract)
    {
        SmartWallet wallet = walletsList.FirstOrDefault(x => x.walletAddress == tx.To);

        if (wallet != null)
        {
            if (tx.ContractAddress == tokenContract)
            {
                // bought | +=
                walletsList.First(x => x.walletAddress == tx.To).bought += double.Parse(tx.TokenValue);
                walletsList.First(x => x.walletAddress == tx.To).txs++;
            }

            //else for if he sold
            //optional. need to think about the logic
        }
        else
        {
            if (tx.ContractAddress == tokenContract)
            {
                // bought | new (received THE token in the final action of the tx)
                walletsList.Add(new SmartWallet(tx.To, double.Parse(tx.TokenValue), 0, "0", "0", 1));
            }
            else
            {
                // sold. Or bought but we missed it
                walletsList.Add(new SmartWallet(tx.To, 0, 0, "0", "0", 1));
            }
        }

        return walletsList;
    }

    public static async Task<double> GetBalanceForWallet(SmartWallet wallet)
    {
        // this one parses all the users profile info. it's used more often when loading the page
        // in browser. maybe it's mode safe to use then
        //https://api.debank.com/hi/user/info?id={address}

        // this one is called only one time per page_reload, but parses less info... will try
        // this one
        //https://api.debank.com/user/total_balance?addr={address}

        string requestUri = "https://api.debank.com/hi/user/info?id=";
        requestUri += wallet.walletAddress;

        HttpResponseMessage response = new();
        using (HttpClient httpClient = new())
        {
            response = await httpClient.GetAsync(requestUri);
        }

        string readRead = await response.Content.ReadAsStringAsync();

        Console.WriteLine(readRead);

        DebankResponse response1 = JsonConvert.DeserializeObject<DebankResponse>(readRead);

        if (response1.data == null)
            return 0;

        Console.WriteLine(response1.data.user.usd_value);

        return response1.data.user.usd_value;
    }

    public class DebankResponse
    {
        public Data data { get; set; }
        public class Data
        {
            public User user { get; set; }
            public class User
            {
                public double usd_value { get; set; }
            }
        }
    }
}

class SmartWallet
{
    public string walletAddress { get; set; }
    public double total_balance { get; set; }
    public double bought { get; set; }
    public double sold { get; set; }
    public string remainingTokenBalance { get; set; } //not calculated yet
    public string PNL { get; set; } //not calculated yet
    public int txs { get; set; }
    public int recentTxs { get; set; }

    public SmartWallet(string walletAddress, double bought, double sold,
                string remainingTokenBalance, string PNL, int txs)
    {
        this.walletAddress = walletAddress;
        this.total_balance = 0;
        this.bought = bought;
        this.sold = sold;
        this.remainingTokenBalance = remainingTokenBalance;
        this.PNL = PNL;
        this.txs = txs;
        this.recentTxs = 0;
    }
}

class Tx
{
    public string Txhash { get; set; }
    public string UnixTimestamp { get; set; }
    public string DateTime { get; set; }
    public string From { get; set; }
    public string To { get; set; }
    public string TokenValue { get; set; }
    public string USDValueDayOfTx { get; set; }
    public string ContractAddress { get; set; }
    public string TokenName { get; set; }
    public string TokenSymbol { get; set; }
}

//"Txhash","UnixTimestamp","DateTime","From","To","TokenValue","USDValueDayOfTx","ContractAddress","TokenName","TokenSymbol"





class Explorer
{
    public static string DetermineNetwork(IList<Tx> listOfTxs)
    {
        List<Network> listOfNetworks = Network.AllNetworks();

        string lastNetwork = "";

        for (int i = 0; i < 3; i++)
        {
            Tx tx = listOfTxs.ElementAt(i);

            for (int j = 0; j < listOfNetworks.Count();)
            {
                Network network = listOfNetworks.ElementAt(j);

                string status = "";
                using (HttpClient httpClient = new())
                {
                    status = GetResponseAsync(httpClient, network, tx).Result;
                }

                if (status == "")
                {
                    listOfNetworks.RemoveAll(x => x.Name == network.Name);
                }
                else if (status == "1" || status == "0")
                {
                    lastNetwork = network.Name;
                    break;
                }
            }
        }

        return lastNetwork;
    }

    private static async Task<string> GetResponseAsync(HttpClient httpClient, Network network, Tx tx)
    {
        TxReceiptResponse receiptResponse = null;
        for (int i = 0; i < 2; i++)
        {
            bool success = true;
            try
            {
                HttpRequestMessage message = new HttpRequestMessage();
                message.RequestUri = new Uri(network.Api + "?module=transaction&action=gettxreceiptstatus&txhash=" +
                tx.Txhash + "&apikey=" + network.Key);

                var response = await httpClient.SendAsync(message);

                var responseReader = response.Content.ReadAsStringAsync().Result;
                receiptResponse = JsonConvert.DeserializeObject<TxReceiptResponse>(responseReader);
            }
            catch (Exception exc)
            {
                Thread.Sleep(3000);

                success = false;
                //SEND MESSAGE OR SMTHING //NEEDS TO BE REFACTORED LATER
            }

            if (success)
                break;
        }

        if (receiptResponse == null)
        {
            //also can be a message, to control errorFlow of the api
            return "failed";
        }

        return receiptResponse.result.status;
    }

    private class TxReceiptResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public Result result { get; set; }
    }
    private class Result
    {
        public string status { get; set; }
    }
}

