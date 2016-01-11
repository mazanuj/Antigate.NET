﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Akumu.Antigate
{
    /// <summary>
    /// Класс реализует работу с сервисом antigate.com
    /// 
    /// </summary>
    public class AntiCaptcha
    {
        /// <summary>
        /// Set/Get Задержка проверки готовности капчи. Стандартно: 15000. (15 сек.)
        /// 
        /// </summary>
        private const int CheckDelay = 15000;

        /// <summary>
        /// Set/Get Кол-во попыток проверки готовности капчи. Стандартно: 30
        /// 
        /// </summary>
        private const int CheckRetryCount = 30;

        /// <summary>
        /// Set/Get кол-во попыток получения нового слота. Стандартно: 3
        /// 
        /// </summary>
        private const int SlotRetry = 3;

        /// <summary>
        /// Set/Get Задержка повторной попытки получения слота на Antigate. Стандартно: 1000
        /// 
        /// </summary>
        private const int SlotRetryDelay = 1000;

        /// <summary>
        /// Сервис антикапчи. Стандартно: antigate.com
        /// 
        /// </summary>
        private const string ServiceProvider = "antigate.com";

        private readonly string Key;

        /// <summary>
        /// Коллекция дополнительных параметров для API запросов.
        /// 
        /// </summary>
        private readonly ParamsContainer Parameters;

        private string CaptchaId;

        /// <summary>
        /// Инициализирует объект AntiCapcha
        /// 
        /// </summary>
        /// <param name="Key">Ваш секретный API ключ</param>
        public AntiCaptcha(string Key)
        {
            if (string.IsNullOrEmpty(Key))
                throw new ArgumentException("Antigate Key is null or empty");
            Parameters = new ParamsContainer();
            this.Key = Key;
        }

        /// <summary>
        /// Отправляет на антигейт файл прочитанный с диска.
        /// 
        /// </summary>
        /// <param name="ImageFilePath">Путь к файлу изображения</param>
        /// <returns>
        /// Разгаданный текст капчи или [null] в случае отсутствия свободных слотов или превышения времени ожидания
        /// </returns>
        public string GetAnswer(string ImageFilePath)
        {
            if (string.IsNullOrEmpty(ImageFilePath))
                throw new ArgumentNullException("Image file path is not set");
            if (!File.Exists(ImageFilePath))
                throw new ArgumentException("Image file does not exist");
            byte[] ImageData;
            try
            {
                using (var image = Image.FromFile(ImageFilePath))
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        image.Save(memoryStream, ImageFormat.Png);
                        ImageData = memoryStream.ToArray();
                    }
                }
            }
            catch
            {
                throw new ArgumentException("Image has unknown file format");
            }
            return GetAnswer(ImageData);
        }

        /// <summary>
        /// Отправляет на антигейт изображение объекта Image
        /// 
        /// </summary>
        /// <param name="Img"/>
        /// <returns/>
        public string GetAnswer(Image Img)
        {
            byte[] ImageData;
            using (var memoryStream = new MemoryStream())
            {
                Img.Save(memoryStream, ImageFormat.Png);
                ImageData = memoryStream.ToArray();
            }
            return GetAnswer(ImageData);
        }

        /// <summary>
        /// Отправляет на антигейт массив данных изображения в формате PNG.
        /// 
        /// </summary>
        /// <param name="ImageData">Массив данных изображения (PNG)</param>
        /// <returns>
        /// Разгаданный текст капчи или [null] в случае отсутствия свободных слотов или превышения времени ожидания
        /// </returns>
        private string GetAnswer(byte[] ImageData)
        {
            if (ImageData == null || ImageData.Length == 0)
                throw new ArgumentException("Image data array is empty");
            var num = SlotRetry;
            CaptchaId = null;
            string str;
            while (true)
            {
                var httpWebRequest =
                    (HttpWebRequest) WebRequest.Create($"http://{ServiceProvider}/in.php");
                httpWebRequest.UserAgent = "Antigate.NET";
                httpWebRequest.Accept = "*/*";
                httpWebRequest.Headers.Add("Accept-Language", "ru");
                httpWebRequest.KeepAlive = true;
                httpWebRequest.AllowAutoRedirect = false;
                httpWebRequest.Method = "POST";
                var Boundary = DateTime.Now.Ticks.ToString("x");
                httpWebRequest.ContentType = $"multipart/form-data; boundary={Boundary}";
                var stringBuilder = new StringBuilder();
                stringBuilder.Append(MultiFormData("method", "post", Boundary));
                stringBuilder.Append(MultiFormData("key", Key, Boundary));
                stringBuilder.Append(MultiFormData("soft_id", "524", Boundary));
                if (Parameters.Count > 0)
                {
                    foreach (Param obj in Parameters.GetParams())
                        stringBuilder.Append(MultiFormData(obj.Key, obj.Value, Boundary));
                }
                stringBuilder.Append(MultiFormDataFile("file", Encoding.Default.GetString(ImageData), "image.png",
                    "image/png", Boundary));
                stringBuilder.Append("--").Append(Boundary).Append("--\r\n\r\n");
                var bytes = Encoding.Default.GetBytes(stringBuilder.ToString());
                httpWebRequest.ContentLength = bytes.Length;
                httpWebRequest.GetRequestStream().Write(bytes, 0, bytes.Length);
                try
                {
                    using (var streamReader = new StreamReader(httpWebRequest.GetResponse().GetResponseStream()))
                    {
                        str = streamReader.ReadToEnd().Trim();
                        streamReader.Close();
                    }
                }
                catch
                {
                    throw new WebException("Antigate server did not respond");
                }
                if (str.Equals("ERROR_NO_SLOT_AVAILABLE", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (num - 1 != 0)
                    {
                        --num;
                        Thread.Sleep(SlotRetryDelay);
                    }
                    else
                        break;
                }
                else
                    goto label_22;
            }
            return null;
            label_22:
            if (str.StartsWith("ERROR_", StringComparison.InvariantCultureIgnoreCase))
                throw new AntigateErrorException(
                    (AntigateError) Enum.Parse(typeof (AntigateError), str.Substring(6)));
            try
            {
                CaptchaId = str.Split(new[]
                {
                    '|'
                }, StringSplitOptions.RemoveEmptyEntries)[1];
            }
            catch
            {
                throw new WebException("Antigate answer is in unknown format or malformed");
            }
            for (var index = 0; index < CheckRetryCount; ++index)
            {
                try
                {
                    Thread.Sleep(CheckDelay);
                    var httpWebRequest =
                        (HttpWebRequest)
                            WebRequest.Create(string.Format("http://{2}/res.php?key={0}&action=get&id={1}",
                                Key, CaptchaId, ServiceProvider));
                    httpWebRequest.UserAgent = "Antigate.NET";
                    httpWebRequest.Accept = "*/*";
                    httpWebRequest.Headers.Add("Accept-Language", "ru");
                    httpWebRequest.KeepAlive = true;
                    httpWebRequest.AllowAutoRedirect = false;
                    httpWebRequest.Method = "GET";
                    var httpWebResponse = (HttpWebResponse) httpWebRequest.GetResponse();
                    using (var streamReader = new StreamReader(httpWebResponse.GetResponseStream()))
                    {
                        str = streamReader.ReadToEnd().Trim();
                        streamReader.Close();
                    }
                    httpWebResponse.Close();

                    if (str.Equals("CAPCHA_NOT_READY", StringComparison.InvariantCultureIgnoreCase)) continue;

                    if (str.StartsWith("ERROR_", StringComparison.InvariantCultureIgnoreCase))
                        throw new AntigateErrorException(
                            (AntigateError) Enum.Parse(typeof (AntigateError), str.Substring(6)));
                    var strArray = str.Split('|');
                    if (strArray[0].Equals("OK", StringComparison.InvariantCultureIgnoreCase))
                        return strArray[1];
                }
                catch
                {
                }
            }
            return null;
        }

        /// <summary>
        /// Оповещаем антигейт о том, что последняя отправленная капча была не верной
        /// 
        /// </summary>
        public void FalseCaptcha()
        {
            if (string.IsNullOrEmpty(CaptchaId))
                throw new ArgumentNullException("Captcha is not solved yet. Nothing to report.");
            try
            {
                var httpWebRequest =
                    (HttpWebRequest)
                        WebRequest.Create($"http://antigate.com/res.php?key={Key}&action=reportbad&id={CaptchaId}");
                httpWebRequest.UserAgent = "Antigate.NET";
                httpWebRequest.Accept = "*/*";
                httpWebRequest.Headers.Add("Accept-Language", "ru");
                httpWebRequest.KeepAlive = true;
                httpWebRequest.AllowAutoRedirect = false;
                httpWebRequest.Method = "GET";
                var httpWebResponse = (HttpWebResponse) httpWebRequest.GetResponse();
                using (
                    var streamReader = new StreamReader(httpWebResponse.GetResponseStream(), Encoding.GetEncoding(1251))
                    )
                {
                    streamReader.ReadToEnd();
                    streamReader.Close();
                }
                httpWebRequest.Abort();
                httpWebResponse.Close();
            }
            catch
            {
                throw new WebException("Error sending the request");
            }
        }

        private static string MultiFormData(string Key, string Value, string Boundary)
        {
            return
                $"--{Boundary}\r\nContent-Disposition: form-data; name=\"{Key}\"\r\n\r\n{Value}\r\n";
        }

        private static string MultiFormDataFile(string Key, string Value, string FileName, string FileType,
            string Boundary)
        {
            return
                string.Format(
                    "--{0}\r\nContent-Disposition: form-data; name=\"{1}\"; filename=\"{3}\"\r\nContent-Type: {4}\r\n\r\n{2}\r\n",
                    Boundary, Key, Value, FileName, FileType);
        }
    }
}