using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using System.Xml;
using System.Text.RegularExpressions;
using System.Text;
using System.IO;
using System.Windows.Media.Imaging;

namespace CursetongueTranslator
{
    public partial class MainPage : UserControl
    {
        //List of all words on all pages of Paranatural
        List<Word> Words = new List<Word>();

        //List of images that make up the visual output
        List<SkullSet> SkullSets = new List<SkullSet>();

        //Maximum number of eyes per skull, representing the chapter number
        //There is currently only support for skulls up to chapter 2
        const int MaxNumberEyesPerSkull = 2;

        //Set when the root element is fully loaded
        bool AllElementsLoaded = false;

        //Enum for the currently displayed translation type
        enum CurrentTranslationEnum { None, ToCursetongue, ToEnglish }

        //Stores the currently displayed translation type
        CurrentTranslationEnum CurrentTranslation = CurrentTranslationEnum.None;

        const string DefaultInputBoxText = "Type English here and then click \"To Cursetongue\" OR type Chapter/Page/Word sets here and then click \"To English\"";

        public MainPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Setup logic that runs when the app loads
        /// </summary>
        private void LayoutRoot_Loaded(object sender, RoutedEventArgs e)
        {
            TextBoxRequest.Visibility = System.Windows.Visibility.Collapsed;
            ButtonToCursetongue.Visibility = System.Windows.Visibility.Collapsed;
            ButtonToEnglish.Visibility = System.Windows.Visibility.Collapsed;

            AboutPage.Visibility = System.Windows.Visibility.Collapsed;

            AllElementsLoaded = true;

            DownloadStringAsync("http://jonkimbel.com/cursetongue/Skim.html");
        }

        /// <summary>
        /// Downloads the given wiki page and strips out words written on it
        /// </summary>
        /// <param name="remoteFilename">URL of the wiki page to download and strip</param>
        public void DownloadStringAsync(string remoteFilename)
        {
            WebClient client = new WebClient();

            //Add our callback to the event handler
            client.DownloadStringCompleted += DownloadStringAsyncCompleted;

            //Run the download
            client.DownloadStringAsync(new Uri(remoteFilename));
        }

        /// <summary>
        /// A callback to be used by DownloadStringAync, strips content from the downloaded wiki page
        /// </summary>
        void DownloadStringAsyncCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            //Let the user know that we're making some progress, especially if it takes a bit to download the mirrored wiki page
            LoadingLabel.Content = "Parsing...";

            if (e.Error == null)
            {
                //If there was no error downloading the skimmed wiki page...

                //Store the whole page in a string
                string result = e.Result;

                //Scrub the page and extract all words in Paranatural
                WikiaPagetoWords(result);
            }
            else
            {
                //Something broke, and we can't really do anything about it
                MessageBox.Show("Error - could not skim Paranatural Wikia's transcription. See translation window for error.");
                TextBoxRequest.Text = e.Error.InnerException.Message + "\n" + e.Error.InnerException.StackTrace;
            }

            //Output all words for debugging purposes
            //TextBlockResult.Text = "";
            //foreach (Word word in Words)
            //    TextBlockResult.Text += word.Text + ";";

            //Show the translation textbox/button
            TextBoxRequest.Visibility = System.Windows.Visibility.Visible;
            ButtonToCursetongue.Visibility = System.Windows.Visibility.Visible;
            ButtonToEnglish.Visibility = System.Windows.Visibility.Visible;
        }

        /// <summary>
        /// Convert HTML skim of Wikia "Comic Transcription" page to a list of Word objects, stored in Words
        /// </summary>
        /// <param name="HTML">Full HTML of Wikia page</param>
        void WikiaPagetoWords(string HTML)
        {
            #region Notes on Wikia page formatting

            //Notes:
                //The transcription starts after "<span class="toctoggle">" (the table of contents)
                //The transcription ends when the "<p>" tags end
                //The chapter name is wrapped in "<h2>" tags
                    //Ex: "Chapter One: The Activity Club and the New Kid"
                //The page number is wrapped in "<h3>" tags
                    //Ex: "Page 1:"

            #endregion

            int chapterIndex = 0;
            int pageIndex = 0;
            int wordIndex = 0;

            //Scan for notable HTML tags
            int startIndex = HTML.IndexOf("<h2><span class=\"mw-headline\"");
            int endIndex = HTML.LastIndexOf("</p>");

            //Handle errors
            if (startIndex < 0)
            {
                TextBlockResult.Text += "Failure to parse: could not find first <h2> tag. Searched for \"" + "<h2><span class=\"mw-headline\"" + "\"";
                return;
            }
            if (endIndex < 0)
            {
                TextBlockResult.Text += "Failure to parse: could not find any <p> tags. Searched for \"" + "</p>" + "\"";
                return;
            }

            //Trim our string down to just the important bits, add a root element so the XmlReader doesn't freak out
            HTML = "<root>" + HTML.Substring(startIndex, endIndex - startIndex + 4) + "</root>";
            //(this wouldn't be necessary if Wikia's HTML was better formatted - at least the wiki body is parsable by an XML reader)

            //Turn the HTML string into a stream for the XML reader
            byte[] byteArray = Encoding.UTF8.GetBytes(HTML);
            MemoryStream stream = new MemoryStream(byteArray);

            //Make an XML reader
            XmlReader xmlReader = XmlReader.Create(stream);

            //Make a boolean stack that we can use to know when to read a tag's contents and when to ignore it
            Stack<bool> useContent = new Stack<bool>();

            //Read the HTML as XML (which it kinda is)
            while (xmlReader.Read())
            {
                //Different behavior for different content...
                switch (xmlReader.NodeType)
                {
                        //Elements are the first half of a tag pair.
                        //We'll push to the useContent stack here so we know what to do with the following text.
                    case XmlNodeType.Element:
                        switch (xmlReader.Name)
                        {
                                //h2 tags denote chapters
                            case "h2":
                                chapterIndex++;
                                pageIndex = 0;
                                wordIndex = 0;
                                goto default;

                                //h3 tags denote pages
                            case "h3":
                                pageIndex++;
                                wordIndex = 0;
                                goto default;

                                //<p>, <u>, and <b> tags can encapsulate valid Cursetongue text
                            case "p":
                            case "u":
                            case "b":
                                //We want to read the following text, so push true
                                useContent.Push(true);
                                break;

                                //Other tags (including <i>) will not encapsulate Cursetongue text, and we should ignore their contents
                            default:
                                //We want to ignore the following text, so push false
                                useContent.Push(false);
                                break;

                        }
                        break;

                        //Text is the content between <tags>
                        //We'll use the topmost value of useContent here, but we won't push/pop it
                    case XmlNodeType.Text:
                        if (useContent.Peek())
                        {
                            //Read the string wrapped by the <p> tag
                            string line = xmlReader.Value;

                            //Trim out the line attribution if there is one, eg. "Max: (words here)"
                            int firstColon = line.IndexOf(':');
                            if (firstColon >= 0)
                                line = line.Substring(firstColon + 1);

                            //Scrub the line of garbage
                            line = line.ScrubInput();

                            //If the line is now empty, it shouldn't go into the word list
                            if (line == "")
                                break;

                            //Iterate through all the words in the <p>aragraph
                            foreach (string word in line.Split(' '))
                            {
                                //Count which word we're on
                                wordIndex++;

                                //Add the word to the List
                                Words.Add(new Word(word, chapterIndex, pageIndex, wordIndex));
                            }
                        }
                        break;

                        //EndElements are the second half of a tag pair.
                        //We'll always pop the useContent stack here.
                    case XmlNodeType.EndElement:
                        useContent.Pop();
                        break;

                        //We don't care about anything else.
                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// UI Handler for the "To Cursetongue" button, looks up the words in the input box and outputs where they are in the script,
        /// and renders some skulls representing these locations
        /// </summary>
        private void ButtonToCursetongue_Click(object sender, RoutedEventArgs e)
        {
            //Read the textbox data and scrub it
            string toTranslate = TextBoxRequest.Text.ScrubInput();

            //Clear previous results
            TextBlockResult.Text = "";

            //Clear previous sprites
            RemoveAllSprites();

            //Iterate through the words making up the string
            foreach (string word in toTranslate.Split(' '))
            {
                List<Word> matches = Words.Where(x => x.Text == word).ToList();
                Word result = new Word("", -1, -1, -1);
                Random rand = new Random();

                //If there are matches, set the result based on the user's selection
                if (matches.Count > 0)
                {
                    ////Use a random match if the user wants that
                    if (RadioRandomWord.IsChecked.Value)
                        result = matches[rand.Next(matches.Count)];
                    else
                        result = matches[0];
                }

                //If the word couldn't be found, let the user know
                if (result.WordIndex == -1)
                {
                    //Output error notification
                    TextBlockResult.Text += "(\'" + word + "\' not found); ";

                    //Create two error skulls
                    Skull skull1 = CreateSkull(-1, -1, -1);
                    Skull skull2 = CreateSkull(-1, -1, -1);

                    //Add the skulls to a 2-skull set, and add that set to the list of all skulls
                    SkullSets.Add(new SkullSet(skull1, skull2));
                }
                //Otherwise, output the chapter/page/word combination
                else
                {
                    //Output text representation of the skull
                    TextBlockResult.Text += "c" + result.ChapterIndex + "p" + result.PageIndex + "w" + result.WordIndex + "; ";

                    //Create image representation of the skull

                    int numberEyes = result.ChapterIndex;

                    //Create first skull - Page #
                    Skull skull1 = CreateSkull(Math.Min(MaxNumberEyesPerSkull, numberEyes), result.PageIndex / 10, result.PageIndex % 10);

                    numberEyes -= Math.Min(MaxNumberEyesPerSkull, numberEyes);

                    //Create second skull - Word #
                    Skull skull2 = CreateSkull(Math.Min(MaxNumberEyesPerSkull, numberEyes), result.WordIndex / 10, result.WordIndex % 10);

                    //Add the skulls to a 2-skull set, and add that set to the list of all skulls
                    SkullSets.Add(new SkullSet(skull1, skull2));
                }
            }

            RenderSkullSets();

            CurrentTranslation = CurrentTranslationEnum.ToCursetongue;
        }

        /// <summary>
        /// Create image objects for all the skulls in SkullSets, adds them to the UI, and arranges them
        /// </summary>
        void RenderSkullSets()
        {
            //Add all of the skulls' component images to the UI
            foreach (SkullSet skullSet in SkullSets)
            {
                foreach (Image image in skullSet.GetImageIterator())
                {
                    ImagePanel.Children.Add(image);
                }
            }

            //Position the skulls on the screen
            ArrangeSkullSets();

            //Update UI
            ImagePanel.UpdateLayout();
        }

        /// <summary>
        /// UI Handler for the "To English" button, looks up the chapter/page/word sets in the input box and outputs the words they represent
        /// along with some image representations of the skulls
        /// </summary>
        private void ButtonToEnglish_Click(object sender, RoutedEventArgs e)
        {
            Regex numberRegex = new Regex(@"\d+");
            MatchCollection Matches = numberRegex.Matches(TextBoxRequest.Text);

            //Clear output
            TextBlockResult.Text = "";
            RemoveAllSprites();

            //If the number of numbers isn't divisible by 3, something is wrong. Tell the user.
            if (Matches.Count % 3 != 0)
            {
                TextBlockResult.Text = "Could not parse input - input should be a series of 3-number sets denoting chapter, page, and word in that order. Example: c1p1w1 c2p1w31";
                return;
            }

            //Iterate through all 3-number sets
            for (int i = 2; i < Matches.Count; i+= 3)
            {
                //Parse the regex'd numbers into real numbers
                int chapterIndex = int.Parse(Matches[i-2].Value);
                int pageIndex = int.Parse(Matches[i-1].Value);
                int wordIndex = int.Parse(Matches[i].Value);

                //Find words corresponding to those numbers
                List<Word> words = Words.Where(x => x.ChapterIndex == chapterIndex && x.PageIndex == pageIndex && x.WordIndex == wordIndex).ToList();

                if (words.Count == 0)
                {
                    //If the word wasn't found, tell the user
                    TextBlockResult.Text += "<c" + chapterIndex + "p" + pageIndex + "w" + wordIndex + " not found> ";

                    //Create two error skulls
                    Skull skull1 = CreateSkull(-1, -1, -1);
                    Skull skull2 = CreateSkull(-1, -1, -1);

                    //Add the skulls to a 2-skull set, and add that set to the list of all skulls
                    SkullSets.Add(new SkullSet(skull1, skull2));
                }
                else
                {
                    //If the word was found, output it
                    TextBlockResult.Text += words[0].Text + " ";

                    //Create image representation of the skull

                    int numberEyes = words[0].ChapterIndex;

                    //Create first skull - Page #
                    Skull skull1 = CreateSkull(Math.Min(MaxNumberEyesPerSkull, numberEyes), words[0].PageIndex / 10, words[0].PageIndex % 10);

                    numberEyes -= Math.Min(MaxNumberEyesPerSkull, numberEyes);

                    //Create second skull - Word #
                    Skull skull2 = CreateSkull(Math.Min(MaxNumberEyesPerSkull, numberEyes), words[0].WordIndex / 10, words[0].WordIndex % 10);

                    //Add the skulls to a 2-skull set, and add that set to the list of all skulls
                    SkullSets.Add(new SkullSet(skull1, skull2));
                }
            }

            RenderSkullSets();

            CurrentTranslation = CurrentTranslationEnum.ToEnglish;
        }

        /// <summary>
        /// Create a single skull sprite from the skull elements stored in Resources
        /// </summary>
        Skull CreateSkull(int numberEyes, int numberHorns, int numberTeeth)
        {
            Skull skull = new Skull();

            #region Validate Input

            if (numberHorns < 0 || numberHorns > 9 ||
                numberTeeth < 0 || numberTeeth > 9 ||
                numberEyes < 0 || numberEyes > 2)
            {
                //If the input is not a valid chapter/page/word, create an error skull
                skull.Base = new Image();
                skull.Base.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("Resources/BaseNotFound.png", UriKind.Relative));
                skull.Base.Width = 64; skull.Base.Height = 64; skull.Base.Stretch = Stretch.None;
                
                return skull;
            }

            #endregion
            
            #region Draw Base

            skull.Base = new Image();
            skull.Base.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("Resources/Base.png", UriKind.Relative));
            skull.Base.Width = 64; skull.Base.Height = 64; skull.Base.Stretch = Stretch.None;

            #endregion

            #region Draw Horns

            if (numberHorns > 0)
            {
                skull.Horns = new Image();
                skull.Horns.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("Resources/Horns" + (numberHorns - 1) + ".png", UriKind.Relative));
                skull.Horns.Width = 64; skull.Horns.Height = 64; skull.Horns.Stretch = Stretch.None;
            }

            #endregion

            #region Draw Eyes

            if (numberEyes > 0)
            {
                skull.Eyes = new Image();
                skull.Eyes.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("Resources/Eyes" + (numberEyes - 1) + ".png", UriKind.Relative));
                skull.Eyes.Width = 64; skull.Eyes.Height = 64; skull.Eyes.Stretch = Stretch.None;
            }

            #endregion

            #region Draw Teeth

            if (numberTeeth > 0)
            {
                skull.Teeth = new Image();
                skull.Teeth.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("Resources/Teeth" + (numberTeeth - 1) + ".png", UriKind.Relative));
                skull.Teeth.Width = 64; skull.Teeth.Height = 64; skull.Teeth.Stretch = Stretch.None;
            }

            #endregion

            return skull;
        }

        /// <summary>
        /// Removes all skulls from the UI and clears the list of SkullSets
        /// </summary>
        void RemoveAllSprites()
        {
            //Removing the images from the layout root makes the UI drop all references to them
            foreach (SkullSet skullSet in SkullSets)
            {
                foreach (Image element in skullSet.GetImageIterator())
                    ImagePanel.Children.Remove(element);
            }

            ImagePanel.UpdateLayout();

            //And now we drop all our references to them
            SkullSets.Clear();
        }

        /// <summary>
        /// UI Handler for when the input box gets focus, clears the sample text there
        /// </summary>
        private void TextBoxRequest_GotFocus(object sender, RoutedEventArgs e)
        {
            //Clear the text box when it is clicked in while set to the default string
            if (TextBoxRequest.Text == DefaultInputBoxText)
                TextBoxRequest.Text = "";
        }

        /// <summary>
        /// UI Handler for when the input box loses focus, adds the sample text if the box is empty
        /// </summary>
        private void TextBoxRequest_LostFocus(object sender, RoutedEventArgs e)
        {
            //Reset the text box to the default string if it loses focus without any text entered
            if (TextBoxRequest.Text == "")
            {
                TextBoxRequest.Text = DefaultInputBoxText;
                CurrentTranslation = CurrentTranslationEnum.None;
            }
        }

        /// <summary>
        /// UI Handler for when the "About" button is pressed, shows the About panel
        /// </summary>
        private void AboutPageButton_Pressed(object sender, MouseButtonEventArgs e)
        {
            //Show the about page
            AboutPage.Visibility = System.Windows.Visibility.Visible;
        }

        /// <summary>
        /// UI Handler for when the "x" button in the About panel is pressed, closes the About panel
        /// </summary>
        private void AboutPageCloseButton_Pressed(object sender, MouseButtonEventArgs e)
        {
            //Hide the about page
            AboutPage.Visibility = System.Windows.Visibility.Collapsed;
        }

        /// <summary>
        /// UI Handler for when the entire Silverlight object changes size, rearranges skulls
        /// </summary>
        private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ArrangeSkullSets();
        }

        /// <summary>
        /// Repositions any rendered skulls in the ImagePanel
        /// </summary>
        void ArrangeSkullSets()
        {
            int xLocation = 0;
            int yLocation = 0;

            if (ImagePanel.ActualWidth < 128)
                return;

            foreach (SkullSet skullSet in SkullSets)
            {
                if (xLocation + 128 > ImagePanel.ActualWidth)
                {
                    //Go to next line, out of room for skulls on this one
                    xLocation = 0;
                    yLocation += 64;
                }
                
                skullSet.SetLocation(xLocation, yLocation);

                xLocation += 128 + 16;  //128 for two skulls' width, 16 for space between skullsets
            }

            ImagePanel.UpdateLayout();
        }

        /// <summary>
        /// UI Handler for when the Random Match/First Match radio buttons change, updates the Cursetongue translation if "To Cursetongue" was selected last
        /// </summary>
        private void RadioButtons_Changed(object sender, RoutedEventArgs e)
        {
            if (!AllElementsLoaded)
                return;

            //Don't want to translate into Cursetongue if the last translation was into English
            if (CurrentTranslation == CurrentTranslationEnum.ToCursetongue)
                    ButtonToCursetongue_Click(sender, e);
        }

        /// <summary>
        /// UI Handler for when the Randomize button is pressed, updates the Cursetongue translation if "To Cursetongue" was selected last
        /// </summary>
        private void ButtonRandomize_Click(object sender, RoutedEventArgs e)
        {
            if (!AllElementsLoaded)
                return;

            //Don't want to translate into Cursetongue if the last translation was into English, only need to re-run if random matches are desired
            if (CurrentTranslation == CurrentTranslationEnum.ToCursetongue && RadioRandomWord.IsChecked.Value)
                    ButtonToCursetongue_Click(sender, e);
        }
    }

    /// <summary>
    /// Extension methods to make the code look a bit cleaner
    /// </summary>
    public static class ExtensionMethods
    {
        /// <summary>
        /// Purge all non-alphanumeric characters in the string, leaving spaces.
        /// </summary>
        public static string RemoveNonAlphanumeric(this string s)
        {
            return new string(s.Where(c => c == ' ' || char.IsLetterOrDigit(c)).ToArray());
        }

        /// <summary>
        /// Scrub input text by removing special characters and leading/trailing whitespace.
        /// Used for scrubbing both the script and the user input field.
        /// </summary>
        public static string ScrubInput(this string s)
        {
            //Purge all punctuation (Cursetongue doesn't use it)
            //Remove leading/trailing whitespace (it's useless)
            //Convert string to lowercase (Cursetongue doesn't have cases)

            return s.RemoveNonAlphanumeric().Trim().ToLower();
        }
    }

    /// <summary>
    /// A data structure for identifying specific words in the Paranatural Archive
    /// </summary>
    struct Word
    {
        public string Text;
        public int ChapterIndex;
        public int PageIndex;
        public int WordIndex;

        /// <summary>
        /// Creates a Word object, filling all properties.
        /// </summary>
        /// <param name="text">The actual word.</param>
        /// <param name="chapterIndex">The chapter the word was found in. (Note: the first chapter is chapter 1, not 0)</param>
        /// <param name="pageIndex">The page the word was on. (Note: the first page is page 1, not 0)</param>
        /// <param name="wordIndex">The offset of the word on its page. (Note: the first word is word 1, not 0)</param>
        public Word(string text, int chapterIndex, int pageIndex, int wordIndex)
        {
            Text = text;
            ChapterIndex = chapterIndex;
            PageIndex = pageIndex;
            WordIndex = wordIndex;
        }
    }

    /// <summary>
    /// A 2-skull word, complete with its index in the sentence it resides in
    /// </summary>
    class SkullSet
    {
        Skull Skull1;
        Skull Skull2;

        /// <summary>
        /// Makes a two-skull word
        /// </summary>
        /// <param name="skull1">The left skull, containing page number</param>
        /// <param name="skull2">The right skull, containing word number</param>
        public SkullSet(Skull skull1, Skull skull2)
        {
            Skull1 = skull1;
            Skull2 = skull2;
        }

        /// <summary>
        /// Sets the location of the word in the ImagePanel
        /// </summary>
        /// <param name="xLocation">Horizontal distance from the top left corner of the ImagePanel</param>
        /// <param name="yLocation">Vertical distance from the top left corner of the ImagePanel</param>
        public void SetLocation(int xLocation, int yLocation)
        {
            if (Skull1 == null || Skull2 == null)
                return;

            Skull1.SetLocation(xLocation, yLocation);
            xLocation += 64;
            Skull2.SetLocation(xLocation, yLocation);
        }

        /// <summary>
        /// Returns an iterator for all of the Images in the set of skulls
        /// </summary>
        public System.Collections.IEnumerable GetImageIterator()
        {
            foreach (Image element in Skull1.GetImageIterator())
                yield return element;

            foreach (Image element in Skull2.GetImageIterator())
                yield return element;
        }
    }

    /// <summary>
    /// A single skull composed of a base, eyes, teeth, and horns
    /// </summary>
    class Skull
    {
        public Image Base;
        public Image Horns;
        public Image Eyes;
        public Image Teeth;

        /// <summary>
        /// Sets the location of the skull in the ImagePanel
        /// </summary>
        /// <param name="xLocation">Horizontal distance from the top left corner of the ImagePanel</param>
        /// <param name="yLocation">Vertical distance from the top left corner of the ImagePanel</param>
        public void SetLocation(int xLocation, int yLocation)
        {
            //Update each image in turn
            if (Base != null)
            {
                Canvas.SetLeft(Base, xLocation);
                Canvas.SetTop(Base, yLocation);
            }

            if (Horns != null)
            {
                Canvas.SetLeft(Horns, xLocation);
                Canvas.SetTop(Horns, yLocation);
            }

            if (Eyes != null)
            {
                Canvas.SetLeft(Eyes, xLocation);
                Canvas.SetTop(Eyes, yLocation);
            }

            if (Teeth != null)
            {
                Canvas.SetLeft(Teeth, xLocation);
                Canvas.SetTop(Teeth, yLocation);
            }
        }

        /// <summary>
        /// Returns an iterator for all of the Images in the skull
        /// </summary>
        public System.Collections.IEnumerable GetImageIterator()
        {
            if (Base != null)
                yield return Base;

            if (Horns != null)
                yield return Horns;

            if (Eyes != null)
                yield return Eyes;

            if (Teeth != null)
                yield return Teeth;
        }
    }
}
