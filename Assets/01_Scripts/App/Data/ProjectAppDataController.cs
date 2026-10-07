/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using System;
using EnigmaVirtual;

/// <summary>
/// Tells the framework's loader which concrete content and settings types this project uses.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class must be authored on a GameObject under <c>Core</c>, or it does nothing.</b>
/// <c>MainController.SetupAppData</c> looks for a controller with
/// <c>GetComponentInChildren&lt;AppDataController&gt;()</c> and only falls back to creating a plain
/// <c>AppDataController</c> when it finds none. An unauthored subclass is simply never instantiated.
/// </para>
/// <para>
/// <b>And the failure is silent, which is why the template ships it wired rather than documented.</b>
/// Without this, the framework deserializes into <c>AppDataBase</c> - one <c>languages</c> field - so
/// <c>AppData.menus</c>, <c>.ctaMedia</c> and <c>.items</c> are never populated. Nothing fails to
/// compile, because <c>MediaWalker</c> is reflective and every consumer takes <c>object</c>. The app
/// simply has no content, and on a site with <c>cleanUnlistedAssets</c> enabled the empty keep-list
/// takes the media cache with it.
/// </para>
/// <para>
/// A branched project keeps this file and edits nothing: it already names <c>AppData</c> and
/// <c>AppSettings</c>, which are the project's own types in <c>App/Model/</c>. Adding a field to those
/// classes needs no change here.
/// </para>
/// </remarks>
public class ProjectAppDataController : AppDataController
{
    protected override Type AppDataType => typeof(AppData);

    protected override Type AppSettingsType => typeof(AppSettings);
}
