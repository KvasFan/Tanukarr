using System;
using System.Linq;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace NzbDrone.Automation.Test.PageModel
{
    public class PageBase
    {
        private readonly IWebDriver _driver;

        public PageBase(IWebDriver driver)
        {
            _driver = driver;
            driver.Manage().Window.Maximize();
        }

        public IWebElement FindByClass(string className, int timeout = 5)
        {
            return Find(By.ClassName(className), timeout);
        }

        public IWebElement Find(By by, int timeout = 5)
        {
            var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeout));
            wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));

            return wait.Until(d =>
            {
                var element = d.FindElement(by);
                return element.Displayed ? element : null;
            });
        }

        public void WaitForNoSpinner(int timeout = 30)
        {
            // give the spinner some time to show up.
            Thread.Sleep(200);

            var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeout));
            wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
            wait.Until(d => d.FindElements(By.CssSelector("div[class*='LoadingIndicator'][class*='rippleContainer']"))
                .All(element => !element.Displayed));
        }

        public IWebElement SeriesNavIcon => Find(By.LinkText("Series"));

        public IWebElement CalendarNavIcon => Find(By.LinkText("Calendar"));

        public IWebElement QueueNavIcon => Find(By.CssSelector("a[href='/activity/queue']"));

        public IWebElement WantedNavIcon => Find(By.LinkText("Wanted"));

        public IWebElement SettingNavIcon => Find(By.LinkText("Settings"));

        public IWebElement SystemNavIcon => Find(By.PartialLinkText("System"));
    }
}
