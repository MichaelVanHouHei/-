using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Flurl.Http;
using HtmlAgilityPack;
using JiebaNet.Analyser;
using JiebaNet.Segmenter;

namespace LearnCarNonCore
{
    class Program
    {
        static CookieSession client  = new CookieSession("https://manwell.clickrapp.com");

        static async Task<bool> Login(string account, string password)
        {
            var result = await client.Request("https://manwell.clickrapp.com/questions_login/login.html").WithHeaders(new
                {
                    content_type = "application/x-www-form-urlencoded",
                    user_agent = " Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.87 Safari/537.36",
                    referer = "https://manwell.clickrapp.com/questions_login.html",//maybe sec check,
                    accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9",
                }).WithAutoRedirect(false)
                .PostStringAsync($"id_number={account}&login_pwd={password}");
            return result.Headers.Contains("Location", "https://manwell.clickrapp.com/questions_dashboard.html");
            //return !(await result.GetStringAsync()).Contains("notification error closeable");
        }
        static CookieJar jar = new CookieJar();
        public class Question
        {
            public string Text { get; set; }
            public string answer { get; set; }
        }

        static   void BookFourMoneyGroupping(BlockingCollection<Question> questions,int bookId)
        {
 
            string bName = $"第{bookId}冊";
            DataSet ds = new DataSet(bName);
            DataTable dt = new DataTable("otherTypeQuestions");
            dt.Columns.Add("question");
            dt.Columns.Add("correct_answer");
            ds.Locale = System.Threading.Thread.CurrentThread.CurrentCulture;
            dt.Locale = System.Threading.Thread.CurrentThread.CurrentCulture;
           
            if (bookId == 4)
            {
                //automatic classify    
                Console.WriteLine("book 4 auto classify");

                Regex r = new Regex(@"[0-9]+" ,RegexOptions.Compiled);
                var hasMoneyQuestion = questions.Where(x => r.IsMatch(x.answer) && x.answer.Contains("元"));
                var groupedHasMoneyQuestion = hasMoneyQuestion.GroupBy(x => x.answer).OrderBy(x=>x.Key.First(d=>char.IsDigit(d)));
                foreach (var groupQ in groupedHasMoneyQuestion)
                {
                    var text = new StringBuilder();
                    var tempT = new DataTable(groupQ.Key);
                    tempT.Columns.Add("question");
                    tempT.Columns.Add("correct_answer");
                    tempT.Locale = System.Threading.Thread.CurrentThread.CurrentCulture;
                    foreach (var q in groupQ)
                    {
                        text.AppendLine(q.Text);
                        tempT.Rows.Add(q.Text,q.answer);
                    }
                  
                    var result = tfidf.ExtractTagsWithWeight(text.ToString(), 10, null);
                
                    Console.WriteLine("正在分析關鍵詞");
                    tempT.Rows.Add("","");
                    tempT.Rows.Add("關鍵詞", "權重");
                    foreach (var k in result)
                    {
                        tempT.Rows.Add(k.Word, k.Weight);
                    }
                    ds.Tables.Add(tempT);
                }
                var otherType = questions.Except(hasMoneyQuestion);
                foreach (var q in otherType)
                {
                  
                    dt.Rows.Add(q.Text, q.answer);
                }
            }
            else
            {
                foreach (var q in questions)
                {
                    dt.Rows.Add(q.Text,q.answer);
                }
                Console.WriteLine("directly to excel");
            }
            ds.Tables.Add(dt);
            ExcelLibrary.DataSetHelper.CreateWorkbook($"{bName}.xls", ds);
            Console.WriteLine($"done---saved :{bName}.xls");
        }
        static TfidfExtractor tfidf = new TfidfExtractor();
        static async Task<BlockingCollection<Question>> CrawlBook(int id = 4)
        {
            //https://manwell.clickrapp.com/new/questions_read/index/1.html?&books=4&p=1
            //since i need book 4 only
            var page = await client.Request($"https://manwell.clickrapp.com/new/questions_read/index/1.html?&books={id}&p=1").WithHeaders(new
                {
                    user_agent = " Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.87 Safari/537.36",
                    referer = "https://manwell.clickrapp.com/questions_login.html",//maybe sec check,
                    accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9",
                }).WithAutoRedirect(false)
                .GetStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(page);
            var maxNode = doc.DocumentNode.Descendants("div")
                .First(x => x.GetAttributeValue("class", "") == "pagination pagination-centered").Descendants("li");
            var max =  int.Parse( maxNode.ElementAt( maxNode.Count() - 2).Descendants("a").First().InnerText);// 下一頁的前一個
            Console.WriteLine($"max page--{max}");
            int current = 1;
            int total = 0;
            BlockingCollection<Question> questions = new BlockingCollection<Question>();
            while (current <=max)
            {
                //parse page 
                Console.WriteLine($"crawling book:{id}--- page:{current} / {max}");
                doc.LoadHtml(page);
                var QuestionRegion = doc.DocumentNode.Descendants("div")
                    .Where(x => x.GetAttributeValue("class", "") == "questions-box-left").Select(
                        y => new Question()
                        {
                            Text=y.Descendants("h2").First().InnerText,
                            //here we get the correct answer only 
                            answer = y.Descendants("li").First(x=>x.GetAttributeValue("class","") == "active").Descendants("span").First().InnerText.Replace(" ","").Replace(",","").Trim(),
                        });
                current++;
                page = await client.Request($"https://manwell.clickrapp.com/new/questions_read/index/1.html?&books={id}&p={current}").WithHeaders(new
                    {
                        user_agent = " Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.87 Safari/537.36",
                        referer = "https://manwell.clickrapp.com/questions_login.html",//maybe sec check,
                        accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9",
                    }).WithAutoRedirect(false)
                    .GetStringAsync();
                total += QuestionRegion.Count();
                foreach (var q in QuestionRegion) questions.Add(q);
                Console.WriteLine($"done---page:{current} / {max} -- total :{total}");
            }
            //denote we get the first page and then determine the last page pagination pagination-centered 
            return questions;
        }

        JiebaSegmenter segmenter = new JiebaSegmenter();
        static void Main(string[] args)
        {
            tfidf.AddStopWords(new List<string>() { "車及", "如實施" ,"車輛", "駕駛員應", "科處" , "通過" , "罰款","罰金"  , "澳門幣" , "車輛" , "駕駛員應", "駕駛" , "累犯" ,"不能"  , "行駛" , "遵守" , "駕駛時" , "規定者", "道路", "違者" });
            //here input the account and password xxxxx,xxx
            var isLogin = Login("xxx", "xxx").GetAwaiter().GetResult();
            if (isLogin)
            {
                Console.WriteLine("well ,login ok");
                int bookId = 4;
                var book = CrawlBook(bookId).GetAwaiter().GetResult();
                BookFourMoneyGroupping(book, bookId);
            }
            else
            {
                Console.WriteLine("Login failed");
            }
            Console.ReadLine();
        }
    }
}
