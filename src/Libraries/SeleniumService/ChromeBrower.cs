using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;

namespace SeleniumService
{
    /// <summary>
    /// Chrome驱动器
    /// </summary>
    public class ChromeBrower
    {
        /// <summary>
        /// Chrome驱动器
        /// </summary>
        public IWebDriver Driver { get; set; }

        /// <summary>
        /// 当前URL地址
        /// </summary>
        public String CurrentUrl
        {
            get
            {
                return Driver.Url;
            }
            set
            {
                Driver.Url = value;
            }
        }


        /// <summary>
        /// 构造函数
        /// </summary>
        public ChromeBrower()
        {

        }

        /// <summary>
        /// 构造函数
        /// </summary>
        ~ChromeBrower()
        {
            if (null != Driver)
            {
                Driver.Quit();
            }
        }

        public string GetCookies()
        {
            var allCookies = Driver.Manage().Cookies.AllCookies;
            var strs = "";
            foreach (var item in allCookies)
            {
                strs += $"{item.Name}={item.Value}; ";
            }
            strs = strs.Trim(';').Trim(' ');
            return strs;
        }

        /// <summary>
        /// 创建Web驱动对象
        /// </summary>
        public void CreateWebDriver()
        {
            if (null != Driver)
                return;

            //Hide console window
            var driverService = ChromeDriverService.CreateDefaultService();
            driverService.HideCommandPromptWindow = true;


            //创建ChromeDriver后，会自动显示空白窗口，设定url为空白
            var option = new ChromeOptions();
            //ChromeOptions options = new ChromeOptions();
            //option.AddArgument("--headless");  // 启用无头模式
            //option.AddArgument("--disable-gpu");  // 禁用 GPU 加速

            // 创建 ChromeDriver
            //IWebDriver driver = new ChromeDriver(options);
            option.AddUserProfilePreference("download.prompt_for_download", false);
            //option.AddArgument("--headless"); //静默

            Driver = new OpenQA.Selenium.Chrome.ChromeDriver(driverService, option);
            //Driver = new OpenQA.Selenium.Chrome.ChromeDriver(option);
            Driver.Url = "about:blank";
            Driver.Manage().Window.Maximize();//浏览器最大化

            //IJavaScriptExecutor js = (IJavaScriptExecutor)Driver;
            //string returnjs = (string)js.ExecuteScript("Object.defineProperties(navigator, {webdriver:{get:()=>undefined}});");
        }

        /// <summary>
        /// 打开浏览器
        /// </summary>
        public void Open()
        {
            if (null == Driver)
                CreateWebDriver();
        }


        /// <summary>
        /// 打开浏览器新的tab页面
        /// </summary>
        /// <param name="url">URL地址</param>
        public void OpenNew(String url)
        {
            if (null == Driver)
            {
                CreateWebDriver();
            }
            Driver.Navigate().GoToUrl(url);
        }

        /// <summary>
        /// 等待页面加载
        /// </summary>
        /// <param name="url"></param>
        public void WaitForUrl(String url)
        {
            while (CurrentUrl.StartsWith(url) == false)
            {
                //Console.WriteLine(CurrentUrl);
                Thread.Sleep(2000);
            }
        }

        /// <summary>
        /// 等待元素加载
        /// </summary>
        /// <param name="url"></param>
        public void WaitElement(String pattern)
        {
            var by = By.XPath(pattern);
            //等待页面加载完 其实就是找一个页面元素是否在了
            new WebDriverWait(Driver, TimeSpan.FromSeconds(3)).Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementExists(by));
        }

        /// <summary>
        /// 等待元素
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="appear">true出现/false消失</param>
        public void WaitElement(string pattern, bool appear = true)
        {
            int elapsedSeconds = 0;
            if (appear)
            {
                while (!ExistElement(pattern) && elapsedSeconds <= 200)
                {
                    Thread.Sleep(200);
                    elapsedSeconds++;
                }
            }
            else
            {
                while (ExistElement(pattern) && elapsedSeconds <= 200)
                {
                    Thread.Sleep(200);
                    elapsedSeconds++;
                }
            }
        }

        /// <summary>
        /// 关闭浏览器
        /// </summary>
        public void Close()
        {
            if (null == Driver)
                return;

            //关闭浏览器
            //driver.Close();
            Driver.Quit();
            Driver = null;
        }

        /// <summary>
        /// 关闭Alert
        /// </summary>
        /// <param name="bAccept">true:确定/false:取消</param>
        public void CloseAlert(bool bAccept)
        {
            if (null == Driver)
            {
                return;
            }

            try
            {
                IAlert alert = Driver.SwitchTo().Alert();
                if (null == alert)
                {
                    return;
                }

                if (bAccept)
                {
                    alert.Accept();
                }
                else
                {
                    alert.Dismiss();
                }
            }
            catch (NoAlertPresentException e)
            {
            }
        }

        public IWebElement SearchElement(String xpath, Action<IWebElement> action = null)
        {
            Func<IWebDriver, IWebElement> waitForSearch = new Func<IWebDriver, IWebElement>((IWebDriver d) =>
            {
                try
                {
                    return Driver.FindElements(By.XPath(xpath)).FirstOrDefault();
                }
                catch
                {
                    return null;
                }
            });

            IWebElement element = WaitForAction(5 * 1000, waitForSearch);
            if (action != null && element != null)
            {
                action(element);
            }
            return element;
        }


        public IWebElement SearchElementBy(By xpath, Action<IWebElement> action = null)
        {
            Func<IWebDriver, IWebElement> waitForSearch = new Func<IWebDriver, IWebElement>((IWebDriver d) =>
            {
                try
                {
                    return Driver.FindElements(xpath).FirstOrDefault();
                }
                catch
                {
                    return null;
                }
            });

            IWebElement element = WaitForAction(5 * 1000, waitForSearch);
            if (action != null && element != null)
            {
                action(element);
            }
            return element;
        }

        public IList<IWebElement> SearchElements(String xpath)
        {
            Func<IWebDriver, List<IWebElement>> waitForSearch = new Func<IWebDriver, List<IWebElement>>((IWebDriver d) =>
            {
                try
                {
                    return Driver.FindElements(By.XPath(xpath)).ToList();
                }
                catch (Exception e)
                {
                    return null;
                }
            });

            var elements = WaitForAction(5 * 1000, waitForSearch);

            return elements;
        }


        public IWebElement SearchElement(IWebElement webElement, String xpath, Action<IWebElement> action = null)
        {
            IWebElement element = webElement.FindElements(By.XPath(xpath)).FirstOrDefault();
            if (action != null && element != null)
            {
                action(element);
            }
            return element;
        }

        public IList<IWebElement> SearchElements(IWebElement webElement, String xpath)
        {
            return webElement.FindElements(By.XPath(xpath)).ToList();
        }

        /// <summary>
        /// 判断控件是否存在
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <returns>true/false</returns>
        public bool ExistElement(string pattern)
        {
            IWebElement input;
            try
            {
                input = Driver.FindElements(By.XPath(pattern)).FirstOrDefault();
            }
            catch
            {
                input = null;
            }
            return (input != null);
        }

        public bool ExistElement(IWebElement webElement, string pattern)
        {
            IWebElement input = SearchElement(webElement, pattern);
            return (input != null);
        }

        /// <summary>
        /// 双击控件
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        public void DoubleClickElement(String pattern)
        {
            Actions a = new Actions(Driver);
            if (null == Driver)
                return;
            IWebElement input = SearchElement(pattern,
            element =>
            {
                a.DoubleClick(element).Perform();
            });
            Thread.Sleep(100);
        }
        /// <summary>
        /// 点击控件
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        public void ClickElement(string pattern)
        {
            if (null == Driver)
                return;

            IWebElement input = SearchElement(pattern,
            element =>
            {
                element.Click();
            });
            Thread.Sleep(300);
        }

        public void ClickElement(IWebElement webElement, string pattern)
        {
            if (null == webElement)
                return;

            IWebElement input = SearchElement(webElement, pattern,
            element =>
            {
                element.Click();
            });
            Thread.Sleep(100);
        }

        /// <summary>
        /// 取得控件属性值
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <param name="attributeName">属性名</param>
        /// <returns>控件属性值</returns>
        /// <todo>在Frame存在的情况下会有问题</todo>
        public String GetElementAttribute(string pattern, string attributeName)
        {
            IWebElement element = SearchElement(pattern);
            if (element == null)
            {
                return null;
            }
            return element.GetAttribute(attributeName);
        }

        public String GetElementAttribute(IWebElement webElement, string pattern, string attributeName)
        {
            IWebElement element = SearchElement(webElement, pattern);
            if (element == null)
            {
                return null;
            }
            return element.GetAttribute(attributeName);
        }

        /// <summary>
        /// 取得控件文本内容
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <returns>文本内容</returns>
        public String GetElementText(string pattern)
        {
            IWebElement element = SearchElement(pattern);
            if (element == null)
            {
                return null;
            }
            return (String)element.Text;
        }

        /// <summary>
        /// 取得控件输入值
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <returns>控件输入值</returns>
        /// <todo>在Frame存在的情况下会有问题</todo>
        public string GetInputValue(string pattern)
        {
            IWebElement input = SearchElement(pattern);
            if (input == null)
            {
                return null;
            }

            return input.GetAttribute("value");
        }

        /// <summary>
        /// 导航到指定URL
        /// </summary>
        /// <param name="url">URL地址</param>
        public void Navigate(string url)
        {
            Func<IWebDriver, string> waitForNav = new Func<IWebDriver, string>((IWebDriver d) =>
            {
                try
                {
                    d.Navigate().GoToUrl(url);
                    return url;
                }
                catch (Exception e)
                {
                    return "";
                }
            });

            string urlLoc = WaitForAction(60 * 1000, waitForNav);
            if (!urlLoc.Equals(""))
            {
                CurrentUrl = urlLoc;
            }
        }

        /// <summary>
        /// 设置复选框值
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <param name="isChecked">选中标识</param>
        /// <param name="failIfNotExist">不存在标识</param>
        public void SetCheckboxValue(string pattern, bool isChecked, bool failIfNotExist)
        {
            IWebElement checkBox = SearchElement(pattern,
                element =>
                {
                    if (element == null || failIfNotExist)
                    {
                        throw new ApplicationException("CheckBox ID: " + pattern + " was not found.");
                    }
                    if (element.Selected != isChecked)
                        element.Click();
                });
        }

        /// <summary>
        /// 设置输入框值(文字)
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <param name="elementValue">输入框值</param>
        public void SetInputStringValue(string pattern, string elementValue)
        {
            IWebElement input = SearchElement(pattern,
                x =>
                {
                    x.Clear();
                    x.SendKeys(elementValue);
                });
        }


        public void SetInputStringValueBy(By pattern, string elementValue)
        {
            IWebElement input = SearchElementBy(pattern,
                x =>
                {
                    x.Clear();
                    x.SendKeys(elementValue);
                });
        }



        /// <summary>
        /// 设置输入框值(数字)
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <param name="elementValue">输入框值</param>
        public void SetInputIntValue(string pattern, int elementValue)
        {
            IWebElement input = SearchElement(pattern,
                x =>
                {
                    x.Clear();
                    x.SendKeys(elementValue.ToString());
                });
        }

        /// <summary>
        /// 设置下拉框选择项(指定选择项索引)
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <param name="index">选择项索引</param>
        public void SelectValueByIndex(string pattern, int index)
        {
            IWebElement element = SearchElement(pattern,
                x =>
                {
                    SelectElement oSelect = new SelectElement(x);
                    oSelect.SelectByIndex(index);
                });
        }

        /// <summary>
        /// 设置下拉框选择项(指定选择项内容)
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <param name="text">选择项内容</param>
        public void SelectByText(string pattern, string text)
        {
            IWebElement element = SearchElement(pattern,
                x =>
                {
                    SelectElement oSelect = new SelectElement(x);
                    oSelect.SelectByText(text);
                });
        }

        /// <summary>
        /// 设置下拉框选择项(指定选择项值)
        /// </summary>
        /// <param name="pattern">控件匹配条件</param>
        /// <param name="value">选择项值</param>
        public void SelectByValue(string pattern, string value)
        {
            IWebElement element = SearchElement(pattern,
                x =>
                {
                    SelectElement oSelect = new SelectElement(x);
                    oSelect.SelectByValue(value);
                });
        }

        /// <summary>
        /// 键盘输入
        /// </summary>
        /// <param name="key">键值</param>
        public void SendKey(string key, String searchPattern)
        {
            IWebElement element = SearchElement(searchPattern,
                x => x.SendKeys(key)
                );
            Thread.Sleep(300);
        }


        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="file">文件路径</param>
        /// <param name="searchPattern">控件匹配条件</param>
        /// <todo>Frame下不兼容</todo>
        public void UploadFile(String file, String searchPattern)
        {
            IWebElement element = SearchElement(searchPattern,
                x => x.SendKeys(file)
                );

            Thread.Sleep(1000);
        }


        /// <summary>
        /// 切换到指定fram
        /// </summary>
        /// <param name="frame">frame控件对象</param>
        public void SwitchToFrame(IWebElement frame)
        {
            if (null == Driver)
                return;
            if (null == frame)
                return;
            Driver.SwitchTo().Frame(frame);
        }

        /// <summary>
        /// 切换到默认控件
        /// </summary>
        public void SwitchToDefault()
        {
            if (null == Driver)
                return;

            Driver.SwitchTo().DefaultContent();
        }

        /// <summary>
        /// 执行JavaScript(无参数)
        /// </summary>
        /// <param name="strScript">JavaScript</param>
        /// <returns>执行结果</returns>
        public bool ExecuteJavaScript(string strScript)
        {
            if (null == Driver)
            {
                return false;
            }

            Func<IWebDriver, bool> waitForScript = new Func<IWebDriver, bool>((IWebDriver d) =>
            {
                try
                {
                    Driver.ExecuteJavaScript(strScript);
                    return true;
                }
                catch
                {
                    return false;
                }
            });

            return WaitForAction(5000, waitForScript);
        }

        /// <summary>
        /// 执行JavaScript(有参数)
        /// </summary>
        /// <param name="strScript">JavaScript</param>
        /// <param name="args">参数</param>
        /// <returns>执行结果</returns>
        public bool ExecuteJavaScript(string strScript, object[] args)
        {
            if (null == Driver)
            {
                return false;
            }

            Func<IWebDriver, bool> waitForScript = new Func<IWebDriver, bool>((IWebDriver d) =>
            {
                try
                {
                    Driver.ExecuteJavaScript(strScript, args);
                    return true;
                }
                catch
                {
                    return false;
                }
            });

            return WaitForAction(5000, waitForScript);
        }

        /// <summary>
        /// 等待(返回WEB控件)
        /// </summary>
        /// <param name="nWaitMS">等待时间(毫秒)</param>
        /// <param name="action">处理函数</param>
        /// <returns>处理函数返回值</returns>
        private IWebElement WaitForAction(UInt32 nWaitMS, Func<IWebDriver, IWebElement> action)
        {
            WebDriverWait wait = new WebDriverWait(Driver, TimeSpan.FromMilliseconds(nWaitMS));
            try
            {
                return wait.Until(action);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 等待(返回控件一览)
        /// </summary>
        /// <param name="nWaitMS">等待时间(毫秒)</param>
        /// <param name="action">处理函数</param>
        /// <returns>处理函数返回值</returns>
        private IList<IWebElement> WaitForAction(UInt32 nWaitMS, Func<IWebDriver, IList<IWebElement>> action)
        {
            WebDriverWait wait = new WebDriverWait(Driver, TimeSpan.FromMilliseconds(nWaitMS));
            try
            {
                return wait.Until(action);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 等待(返回string)
        /// </summary>
        /// <param name="nWaitMS">等待时间(毫秒)</param>
        /// <param name="action">处理函数</param>
        /// <returns>处理函数返回值</returns>
        private string WaitForAction(UInt32 nWaitMS, Func<IWebDriver, string> action)
        {
            WebDriverWait wait = new WebDriverWait(Driver, TimeSpan.FromMilliseconds(nWaitMS));
            try
            {
                return wait.Until(action);
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// 等待(返回bool)
        /// </summary>
        /// <param name="nWaitMS">等待时间(毫秒)</param>
        /// <param name="action">处理函数</param>
        /// <returns>处理函数返回值</returns>
        private bool WaitForAction(UInt32 nWaitMS, Func<IWebDriver, bool> action)
        {
            WebDriverWait wait = new WebDriverWait(Driver, TimeSpan.FromMilliseconds(nWaitMS));
            try
            {
                wait.Until(action);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }


    }
}
