# About

This project contains various localization-related utilities I use across various projects. The functionalities aren't cleanly demarcated right now, but there are two significant functionalities: one to batch-download Google Drive spreadsheets as CSVs (useful even for non-localization projects), and one to transform CSVs with translation maps into C# source code using the [LString class from BagoumLib](https://github.com/Bagoum/suzunoya/blob/master/BagoumLib/Culture/Variant.cs). 

# Downloading Spreadsheets

Google Sheets allows you to download a sheet as a .csv file, but only one sheet at a time. If you want to update multiple sheets, you have to click through the menus once for every sheet. Because iteration time should always be effectively O(1), this is unacceptable for any real usage of Google Sheets (such as as translation maps). To remedy this, I wrote a bunch of utilities that first use Google Apps Script to turn all the sheets into .csvs on your Google Drive, then zip up the folder, then download and unzip the folder to your computer.

Note that in the process of getting the download process working, you will have to give a lot of permissions and consents, but you will be giving them all to yourself. I am not receiving your information in any way.

First, create an app on Google Cloud Platform. On the OAuth Consent Screen page, in the Scopes section, give it the sensitive scopes of /auth/spreadsheets and /auth/drive. In the Test users section, add yourself if you're not already there.

<img src=".\img\scopes.jpg" alt="scopes" style="zoom:50%;" />

Then, on the Credentials page, create an OAuth 2.0 Client ID. Get its JSON file and place it at the GCP_CLIENT_AUTH path in LocalizationSpreadsheeter/Program.cs (feel free to change the path). Or you can create that file manually, with the structure:

```
{
  "installed": {
    "client_id": "oauth client id goes here",
    "client_secret":"oauth client secret goes here"
  }
}
```

Third, on Google Apps Script, create a new project and add your Google Cloud Platform project number to it.

<img src=".\img\id.jpg" alt="id" style="zoom: 25%;" />

Then, create a file and add the code in exportAsCSVAppsScript.js to it. Once you've saved the file, go ahead and create a new deployment.

<img src=".\img\deploy.jpg" alt="deploy" style="zoom: 25%;" />

The "deployment id" on the next page can be entered in the SCRIPT_ID constant in LocalizationSpreadsheeter/Program.cs. 

Now you're ready to download your spreadsheets. You can call the Download function in Program.cs, which exports the spreadsheets to a zip of CSV files on Google Drive, then downloads the archive locally and unzips it. It will require several screens of OAuth permissions that should automatically open in your browser.

To download spreadsheets with the `Download` function, you only need a spreadsheet ID from Google Drive and an output CSV directory. To get the ID of a spreadsheet, open it in a browser and look at the url. In the url `https://docs.google.com/spreadsheets/d/HELLOWORLDFOOBAR/`, `HELLOWORLDFOOBAR` is the spreadsheet ID. The default spreadsheet information in `dmkSpreadsheet` points to a public copy of the localized strings for DMK/SiMP that you can use to test.

If you're only using the download functionality, make sure to remove the `generateAll` call in Main, which will run the localization parser (see below).

# Localization Maps

Let's say we have a spreadsheet formatted like this:

| Key        | English | Notes                                           | Japanese |
| ---------- | ------- | ----------------------------------------------- | -------- |
| animal.dog | dog     | wow. such word. many translate.                 | 犬       |
| animal.cat | cat     | Cats seem cool but I've never actually met one. | 猫       |

The LocalizationExecutor project can transform a set of such spreadsheets into source code:

```c#
private static readonly Dictionary<string, LString> _allDataMap = new() {
	{ "animal.dog", animal_dog },
	{ "animal.cat", animal_cat }
};


public static readonly LString animal_dog = new LString("dog", (Locales.JP, "犬"))
	{ ID = "animal.dog" };
	
public static readonly LString animal_cat = new LString("cat", (Locales.JP, "猫"))
	{ ID = "animal.cat" };
```

This means that you can access these localized strings while *knowing they exist at compile time*. Alternatively, you can implement a runtime lookup via _allDataMap. If you need to map the strings to something else-- say voicelines-- you can do so via the ID, which is preserved in the final LString.

Furthermore, this project supports **string functions**:

| Key         | English                                          | Notes                | Japanese                                  |
| ----------- | ------------------------------------------------ | -------------------- | ----------------------------------------- |
| pickup_gold | {0} picked up {1} gold {$PLURAL(1, coin, coins)} | 0 = actor, 1 = count | {0}が金貨を{$JP_COUNTER(1, 枚)}拾いました |

In this case, we want to write a generic function that deals with a sort of RPG situation where a certain character picks up a variable number of coins. Not only do we need to interpolate the character name and number into the string, we also need to deal with grammatical problems:

- In English, the plurality of the number changes the conjugation of "coin".
- In Japanese, the number changes the pronunciation of the counter (In a purely written case, we wouldn't need to handle such a variation, but take it as an example).

The parser supports raw C# string interpolation, as well as invocation of external methods that can deal with the language-specific grammatical issues. For example, it would turn this entry in the spreadsheet into the following function:

```c#
public static string pickup_gold(object arg0, object arg1) => Localization.Locale.Value switch {
	Locales.JP => Render(Localization.Locale.Value, new[] {
		"{0}",
		"が金貨を",
		JP_COUNTER(arg1, "枚"),
		"拾いました",
	}, arg0, arg1),
	_ => Render(Localization.Locale.Value, new[] {
		"{0}",
		" picked up ",
		"{1}",
		" gold ",
		PLURAL(arg1, "coin", "coins"),
	}, arg0, arg1),
};
```

Render is essentially a helper function that concatenates strings and treats them as a format string. You can see a reference definition [here](https://github.com/Bagoum/suzunoya/blob/master/BagoumLib/Culture/LocalizationRendering.cs).

We can define our helper functions somewhere else as follows:

```c#
public static string PLURAL(object arg, string singular, string plural) =>
    Convert.ToDouble(arg) == 1 ? singular : plural;
```

Then, we can use this as a function:

```c#
var kasen = new LString("Kasen", (Locales.JP, "華扇"));
Localization.Locale.Value = Locales.EN;
Assert.AreEqual("Kasen picked up 50 gold coins", pickup_gold(kasen, 50));
Assert.AreEqual("Kasen picked up 1 gold coin", pickup_gold(kasen, 1));
Localization.Locale.Value = Locales.JP;
Assert.AreEqual("華扇が金貨を50枚拾いました", pickup_gold(kasen, 50));
Assert.AreEqual("華扇が金貨を1枚拾いました", pickup_gold(kasen, 1));
```

These functions are decently compile-time safe (though there's no type-checking on the arguments, which we might be able to get if we wrote them manually), are fairly fast (no reflection hijinks, no string lookup), and can actually be written in your translation spreadsheets.

### Code Setup

To do localization mapping, you need a `SpreadsheetCtx`, which contains a list of batches (in addition to the spreadsheet ID and output CSV directory). A batch `FileBatch` contains metadata about some subset of the CSVs on Google Drive. When processing a batch, the code will take all the files marked by that batch and put the generated code in the directory `batch.outDir`. 

As an example, the DMK spreadsheet contains strings for the DMK engine as well as the SiMP game. The DMK code should go in the core Unity repository, but the SiMP code should go in the SiMP submodule. To handle this, the `dmkSpreadsheet` has two batches `dmkCoreBatch` and `dmkSimpBatch`, which handle different sets of information and point to different output directories.

# Extra Configuration

If the columns of your spreadsheet are structured differently, or you need more languages, you can set up a custom CSVProvider type and override `loadRows` and `locales` in `LGenCtx`. See `LocalizerPolyglot.fs` for an example on how to use a custom spreadsheet structure.

The string function parser is defined in LocalizationParser.fs using FParsec.

Each sheet is mapped to its own source file with its own nested class via LocalizationCodeGen.fs. Then, a single file with the default name `_StringRepository.cs` is constructed with the `_allDataMap` as seen in the previous section. You probably want some helpers to query `_allDataMap`; I recommend putting them in a partial static class (along with your Render functions). See https://github.com/Bagoum/danmokou/blob/master/Assets/Danmokou/Plugins/Danmokou/Core/Localization/LocalizedStrings.cs for a reference on querying.



This document is WIP-- ping me on Discord if you need to know more.