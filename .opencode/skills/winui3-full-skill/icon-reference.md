# WinUI 3 Icon Reference — Segoe Fluent Icons & Segoe MDL2 Assets

## Quick Reference

- **Windows 11 recommends `Segoe Fluent Icons`** over `Segoe MDL2 Assets` (Win10 font)
- In WinUI 3, the default `FontFamily` for icons is already `Segoe Fluent Icons` via `{StaticResource SymbolThemeFontFamily}`
- **E7–E9 glyphs are shared** between both fonts (same code points, Fluent has updated artwork)
- **EA+ range** has many Fluent-only additions not in MDL2
- **Preferred icon sizes**: 16, 20, 24, 32, 40, 48, 64 (other sizes may appear blurry)
- **E0–E5 range is legacy/deprecated** — do not use

### Usage

```xml
<!-- Recommended: use SymbolThemeFontFamily (resolves to Segoe Fluent Icons on Win11) -->
<FontIcon Glyph="&#xE700;"/>

<!-- Explicit font (use only if targeting Win10 specifically with MDL2) -->
<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE700;"/>
<FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE700;"/>

<!-- Symbol enum (limited subset, no FontFamily needed) -->
<SymbolIcon Symbol="GlobalNavigationButton"/>

<!-- Layering example (outline + fill) -->
<Grid>
    <FontIcon Glyph="&#xEB52;" Foreground="#C72335"/>
    <FontIcon Glyph="&#xEB51;"/>
</Grid>
```

---

## Navigation

| Name | Unicode | Glyph |
|------|---------|-------|
| Back | E72B | `&#xE72B;` |
| Forward | E72A | `&#xE72A;` |
| ChromeBack | E830 | `&#xE830;` |
| Home | E80F | `&#xE80F;` |
| HomeSolid | EA8A | `&#xEA8A;` |
| GlobalNavButton | E700 | `&#xE700;` |
| AllApps | E71D | `&#xE71D;` |
| ChevronDown | E70D | `&#xE70D;` |
| ChevronUp | E70E | `&#xE70E;` |
| ChevronLeft | E76B | `&#xE76B;` |
| ChevronRight | E76C | `&#xE76C;` |
| ChevronDownSmall | E96E | `&#xE96E;` |
| ChevronUpSmall | E96D | `&#xE96D;` |
| ChevronLeftSmall | E96F | `&#xE96F;` |
| ChevronRightSmall | E970 | `&#xE970;` |
| ChevronDownMed | E972 | `&#xE972;` |
| ChevronUpMed | E971 | `&#xE971;` |
| ChevronLeftMed | E973 | `&#xE973;` |
| ChevronRightMed | E974 | `&#xE974;` |
| Up | E74A | `&#xE74A;` |
| Down | E74B | `&#xE74B;` |
| Previous | E892 | `&#xE892;` |
| Next | E893 | `&#xE893;` |
| PageLeft | E760 | `&#xE760;` |
| PageRight | E761 | `&#xE761;` |
| Go | E8AD | `&#xE8AD;` |
| GoToStart | E8FC | `&#xE8FC;` |
| DockLeft | E90C | `&#xE90C;` |
| DockRight | E90D | `&#xE90D;` |
| DockBottom | E90E | `&#xE90E;` |

## Actions

| Name | Unicode | Glyph |
|------|---------|-------|
| Add | E710 | `&#xE710;` |
| Remove | E738 | `&#xE738;` |
| Cancel | E711 | `&#xE711;` |
| Edit | E70F | `&#xE70F;` |
| Delete | E74D | `&#xE74D;` |
| Save | E74E | `&#xE74E;` |
| SaveAs | E792 | `&#xE792;` |
| SaveCopy | EA35 | `&#xEA35;` |
| Copy | E8C8 | `&#xE8C8;` |
| Cut | E8C6 | `&#xE8C6;` |
| Paste | E77F | `&#xE77F;` |
| Undo | E7A7 | `&#xE7A7;` |
| Redo | E7A6 | `&#xE7A6;` |
| Rename | E8AC | `&#xE8AC;` |
| Refresh | E72C | `&#xE72C;` |
| Search | E721 | `&#xE721;` |
| Filter | E71C | `&#xE71C;` |
| Sort | E8CB | `&#xE8CB;` |
| Zoom | E71E | `&#xE71E;` |
| ZoomIn | E8A3 | `&#xE8A3;` |
| ZoomOut | E71F | `&#xE71F;` |
| SelectAll | E8B3 | `&#xE8B3;` |
| Clear | E894 | `&#xE894;` |
| ClearSelection | E8E6 | `&#xE8E6;` |
| OpenWith | E7AC | `&#xE7AC;` |
| OpenFile | E8E5 | `&#xE8E5;` |
| Print | E749 | `&#xE749;` |
| Crop | E7A8 | `&#xE7A8;` |
| Rotate | E7AD | `&#xE7AD;` |
| Move | E7C2 | `&#xE7C2;` |
| Tag | E8EC | `&#xE8EC;` |
| Attach | E723 | `&#xE723;` |
| Link | E71B | `&#xE71B;` |
| Scan | E8FE | `&#xE8FE;` |
| Highlight | E7E6 | `&#xE7E6;` |
| Trim | E78A | `&#xE78A;` |
| Export | EDE1 | `&#xEDE1;` |

## Communication

| Name | Unicode | Glyph |
|------|---------|-------|
| Mail | E715 | `&#xE715;` |
| MailFill | E8A8 | `&#xE8A8;` |
| MailReply | E8CA | `&#xE8CA;` |
| MailReplyAll | E8C2 | `&#xE8C2;` |
| MailForward | E89C | `&#xE89C;` |
| Phone | E717 | `&#xE717;` |
| PhoneBook | E780 | `&#xE780;` |
| VideoChat | E8AA | `&#xE8AA;` |
| Message | E8BD | `&#xE8BD;` |
| ChatBubbles | E8F2 | `&#xE8F2;` |
| Send | E724 | `&#xE724;` |
| SendFill | E725 | `&#xE725;` |
| HangUp | E778 | `&#xE778;` |
| IncomingCall | E77E | `&#xE77E;` |
| Comment | E90A | `&#xE90A;` |
| LeaveChat | E89B | `&#xE89B;` |
| PostUpdate | E8F3 | `&#xE8F3;` |
| Dialpad | E75F | `&#xE75F;` |
| CallForwarding | E7F2 | `&#xE7F2;` |
| ForwardCall | EAC2 | `&#xEAC2;` |

## Media

| Name | Unicode | Glyph |
|------|---------|-------|
| Play | E768 | `&#xE768;` |
| Pause | E769 | `&#xE769;` |
| Stop | E71A | `&#xE71A;` |
| Record | E7C8 | `&#xE7C8;` |
| Record2 | EA3F | `&#xEA3F;` |
| Previous | E892 | `&#xE892;` |
| Next | E893 | `&#xE893;` |
| FastForward | EB9D | `&#xEB9D;` |
| Rewind | EB9E | `&#xEB9E;` |
| Shuffle | E8B1 | `&#xE8B1;` |
| RepeatOne | E8ED | `&#xE8ED;` |
| RepeatAll | E8EE | `&#xE8EE;` |
| Volume | E767 | `&#xE767;` |
| Volume0 | E992 | `&#xE992;` |
| Volume1 | E993 | `&#xE993;` |
| Volume2 | E994 | `&#xE994;` |
| Volume3 | E995 | `&#xE995;` |
| Mute | E74F | `&#xE74F;` |
| VolumeDisabled | EA85 | `&#xEA85;` |
| Camera | E722 | `&#xE722;` |
| Webcam | E8B8 | `&#xE8B8;` |
| Microphone | E720 | `&#xE720;` |
| MicOn | EC71 | `&#xEC71;` |
| MicOff | EC54 | `&#xEC54;` |
| Video | E714 | `&#xE714;` |
| VideoSolid | EA0C | `&#xEA0C;` |
| Audio | E8D6 | `&#xE8D6;` |
| Slideshow | E786 | `&#xE786;` |
| MusicNote | EC4F | `&#xEC4F;` |
| MusicAlbum | E93C | `&#xE93C;` |
| Movies | E8B2 | `&#xE8B2;` |
| Media | EA69 | `&#xEA69;` |
| Streaming | E93E | `&#xE93E;` |
| Equalizer | E9E9 | `&#xE9E9;` |
| CC (Closed Captions) | E7F0 | `&#xE7F0;` |
| PlaybackRate1x | EC57 | `&#xEC57;` |
| PlaybackRateOther | EC58 | `&#xEC58;` |
| StopSolid | EE95 | `&#xEE95;` |

## Status & Feedback

| Name | Unicode | Glyph |
|------|---------|-------|
| Info | E946 | `&#xE946;` |
| Info2 | EA1F | `&#xEA1F;` |
| Error | E783 | `&#xE783;` |
| ErrorBadge | EA39 | `&#xEA39;` |
| Warning | E7BA | `&#xE7BA;` |
| CheckMark | E73E | `&#xE73E;` |
| Completed | E930 | `&#xE930;` |
| CompletedSolid | EC61 | `&#xEC61;` |
| Sync | E895 | `&#xE895;` |
| SyncError | EA6A | `&#xEA6A;` |
| SyncFolder | E8F7 | `&#xE8F7;` |
| Processing | E9F5 | `&#xE9F5;` |
| ProgressRingDots | F16A | `&#xF16A;` |
| StatusCircle | EA81 | `&#xEA81;` |
| StatusError | EA83 | `&#xEA83;` |
| StatusErrorFull | EB90 | `&#xEB90;` |
| StatusWarning | EA84 | `&#xEA84;` |
| StatusTriangle | EA82 | `&#xEA82;` |
| StatusCheckmark | F1D8 | `&#xF1D8;` |
| StatusInfo | F3CC | `&#xF3CC;` |
| StatusCircleCheckmark | F13E | `&#xF13E;` |
| StatusCircleErrorX | F13D | `&#xF13D;` |
| StatusCircleInfo | F13F | `&#xF13F;` |
| StatusCircleBlock | F140 | `&#xF140;` |
| StatusCircleSync | F143 | `&#xF143;` |
| Important | E8C9 | `&#xE8C9;` |
| Shield | EA18 | `&#xEA18;` |
| Certificate | EB95 | `&#xEB95;` |
| Blocked | E733 | `&#xE733;` |
| Help | E897 | `&#xE897;` |
| Fingerprint | E928 | `&#xE928;` |
| Lightbulb | EA80 | `&#xEA80;` |
| Bug | EBE8 | `&#xEBE8;` |
| Unknown | E9CE | `&#xE9CE;` |
| Accept | E8FB | `&#xE8FB;` |

## Files & Folders

| Name | Unicode | Glyph |
|------|---------|-------|
| Folder | E8B7 | `&#xE8B7;` |
| FolderFill | E8D5 | `&#xE8D5;` |
| FolderOpen | E838 | `&#xE838;` |
| NewFolder | E8F4 | `&#xE8F4;` |
| FolderHorizontal | F12B | `&#xF12B;` |
| MoveToFolder | E8DE | `&#xE8DE;` |
| OpenLocal | E8DA | `&#xE8DA;` |
| Document | E8A5 | `&#xE8A5;` |
| Page | E7C3 | `&#xE7C3;` |
| PageSolid | E729 | `&#xE729;` |
| ProtectedDocument | E8A6 | `&#xE8A6;` |
| PDF | EA90 | `&#xEA90;` |
| SaveLocal | E78C | `&#xE78C;` |
| Download | E896 | `&#xE896;` |
| Upload | E898 | `&#xE898;` |
| CloudDownload | EBD3 | `&#xEBD3;` |
| Cloud | E753 | `&#xE753;` |
| Package | E7B8 | `&#xE7B8;` |
| ZipFolder | F012 | `&#xF012;` |
| OpenInNewWindow | E8A7 | `&#xE8A7;` |
| NewWindow | E78B | `&#xE78B;` |
| Preview | E8FF | `&#xE8FF;` |
| BrowsePhotos | E7C5 | `&#xE7C5;` |
| Photo | E91B | `&#xE91B;` |
| Photo2 | EB9F | `&#xEB9F;` |
| Picture | E8B9 | `&#xE8B9;` |
| FileExplorer | EC50 | `&#xEC50;` |
| SaveAs | E792 | `&#xE792;` |
| HardDrive | EDA2 | `&#xEDA2;` |
| ReportDocument | E9F9 | `&#xE9F9;` |

## People & Social

| Name | Unicode | Glyph |
|------|---------|-------|
| People | E716 | `&#xE716;` |
| Contact | E77B | `&#xE77B;` |
| Contact2 | E8D4 | `&#xE8D4;` |
| ContactSolid | EA8C | `&#xEA8C;` |
| ContactInfo | E779 | `&#xE779;` |
| Group | E902 | `&#xE902;` |
| AddFriend | E8FA | `&#xE8FA;` |
| FavoriteStar | E734 | `&#xE734;` |
| FavoriteStarFill | E735 | `&#xE735;` |
| Unfavorite | E8D9 | `&#xE8D9;` |
| Heart | EB51 | `&#xEB51;` |
| HeartFill | EB52 | `&#xEB52;` |
| Like | E8E1 | `&#xE8E1;` |
| LikeSolid | F3BF | `&#xF3BF;` |
| Dislike | E8E0 | `&#xE8E0;` |
| LikeDislike | E8DF | `&#xE8DF;` |
| Share | E72D | `&#xE72D;` |
| Reshare | E8EB | `&#xE8EB;` |
| SwitchUser | E748 | `&#xE748;` |
| OtherUser | E7EE | `&#xE7EE;` |
| GuestUser | EE57 | `&#xEE57;` |
| Admin | E7EF | `&#xE7EF;` |
| Accounts | E910 | `&#xE910;` |
| BlockContact | E8F8 | `&#xE8F8;` |
| Family | EBDA | `&#xEBDA;` |
| ShoppingCart | E7BF | `&#xE7BF;` |
| Shop | E719 | `&#xE719;` |

## Devices & Hardware

| Name | Unicode | Glyph |
|------|---------|-------|
| PC1 | E977 | `&#xE977;` |
| Tablet | E70A | `&#xE70A;` |
| CellPhone | E8EA | `&#xE8EA;` |
| TVMonitor | E7F4 | `&#xE7F4;` |
| MobileTablet | E8CC | `&#xE8CC;` |
| LaptopSelected | EC76 | `&#xEC76;` |
| Keyboard | E92E | `&#xE92E;` |
| Mouse | E962 | `&#xE962;` |
| USB | E88E | `&#xE88E;` |
| Bluetooth | E702 | `&#xE702;` |
| Wifi | E701 | `&#xE701;` |
| Ethernet | E839 | `&#xE839;` |
| Network | E968 | `&#xE968;` |
| NetworkSharing | F193 | `&#xF193;` |
| VPN | E705 | `&#xE705;` |
| Devices | E772 | `&#xE772;` |
| Devices2 | E975 | `&#xE975;` |
| Devices3 | EA6C | `&#xEA6C;` |
| Project | EBC6 | `&#xEBC6;` |
| Connect | E703 | `&#xE703;` |
| Monitor | E7F9 | `&#xE7F9;` |
| Projector | E95D | `&#xE95D;` |
| Headphone | E7F6 | `&#xE7F6;` |
| Headset | E95B | `&#xE95B;` |
| Speakers | E7F5 | `&#xE7F5;` |
| Print | E749 | `&#xE749;` |
| SDCard | E7F1 | `&#xE7F1;` |
| GameConsole | E967 | `&#xE967;` |
| Game | E7FC | `&#xE7FC;` |
| Sensor | E957 | `&#xE957;` |
| Touchscreen | EDA4 | `&#xEDA4;` |
| RAM | EEA0 | `&#xEEA0;` |
| CPU | EEA1 | `&#xEEA1;` |
| HardDrive | EDA2 | `&#xEDA2;` |
| NetworkAdapter | EDA3 | `&#xEDA3;` |
| Touchpad | EFA5 | `&#xEFA5;` |
| Smartcard | E963 | `&#xE963;` |
| Wire | E95F | `&#xE95F;` |
| GatewayRouter | EB77 | `&#xEB77;` |

## Settings & System

| Name | Unicode | Glyph |
|------|---------|-------|
| Settings | E713 | `&#xE713;` |
| System | E770 | `&#xE770;` |
| Personalize | E771 | `&#xE771;` |
| Lock | E72E | `&#xE72E;` |
| Unlock | E785 | `&#xE785;` |
| UpdateRestore | E777 | `&#xE777;` |
| PowerButton | E7E8 | `&#xE7E8;` |
| Brightness | E706 | `&#xE706;` |
| LowerBrightness | EC8A | `&#xEC8A;` |
| RotationLock | E755 | `&#xE755;` |
| Airplane | E709 | `&#xE709;` |
| AirplaneSolid | EB4C | `&#xEB4C;` |
| QuietHours | E708 | `&#xE708;` |
| Ringer | EA8F | `&#xEA8F;` |
| CommandPrompt | E756 | `&#xE756;` |
| ThisPC | EC4E | `&#xEC4E;` |
| TaskView | E7C4 | `&#xE7C4;` |
| Repair | E90F | `&#xE90F;` |
| Manage | E912 | `&#xE912;` |
| Permissions | E8D7 | `&#xE8D7;` |
| ActionCenter | E91C | `&#xE91C;` |
| ActionCenterNotification | E7E7 | `&#xE7E7;` |
| InPrivate | E727 | `&#xE727;` |
| DeveloperTools | EC7A | `&#xEC7A;` |
| DefenderApp | E83D | `&#xE83D;` |
| Diagnostics | E9D9 | `&#xE9D9;` |
| ResetDevice | ED10 | `&#xED10;` |
| Uninstall | EC91 | `&#xEC91;` |
| SignOut | F3B1 | `&#xF3B1;` |
| PasswordKeyShow | E9A8 | `&#xE9A8;` |
| PasswordKeyHide | E9A9 | `&#xE9A9;` |
| BatterySaver | F432 | `&#xF432;` |
| ScreenTime | F182 | `&#xF182;` |
| Widget | F034 | `&#xF034;` |
| WidgetGear | F035 | `&#xF035;` |
| WidgetAdd | F036 | `&#xF036;` |

## UI Controls

| Name | Unicode | Glyph |
|------|---------|-------|
| Checkbox | E739 | `&#xE739;` |
| CheckboxComposite | E73A | `&#xE73A;` |
| CheckboxFill | E73B | `&#xE73B;` |
| CheckboxIndeterminate | E73C | `&#xE73C;` |
| RadioBullet | E915 | `&#xE915;` |
| RadioBtnOff | ECCA | `&#xECCA;` |
| RadioBtnOn | ECCB | `&#xECCB;` |
| ToggleFilled | EC11 | `&#xEC11;` |
| ToggleBorder | EC12 | `&#xEC12;` |
| ToggleThumb | EC14 | `&#xEC14;` |
| ToggleLeft | F19E | `&#xF19E;` |
| ToggleRight | F19F | `&#xF19F;` |
| Pin | E718 | `&#xE718;` |
| PinFill | E841 | `&#xE841;` |
| Unpin | E77A | `&#xE77A;` |
| Pinned | E840 | `&#xE840;` |
| PinnedFill | E842 | `&#xE842;` |
| More | E712 | `&#xE712;` |
| FullScreen | E740 | `&#xE740;` |
| BackToWindow | E73F | `&#xE73F;` |
| ChromeMinimize | E921 | `&#xE921;` |
| ChromeMaximize | E922 | `&#xE922;` |
| ChromeRestore | E923 | `&#xE923;` |
| ChromeClose | E8BB | `&#xE8BB;` |
| ChromeFullScreen | E92D | `&#xE92D;` |
| ChromeBackToWindow | E92C | `&#xE92C;` |
| MultiSelect | E762 | `&#xE762;` |
| ExpandTile | E976 | `&#xE976;` |
| MiniExpand | E93A | `&#xE93A;` |
| SliderThumb | EC13 | `&#xEC13;` |
| View | E890 | `&#xE890;` |
| ViewAll | E8A9 | `&#xE8A9;` |
| ShowResults | E8BC | `&#xE8BC;` |
| GridView | F0E2 | `&#xF0E2;` |
| GridViewSmall | F232 | `&#xF232;` |
| List | EA37 | `&#xEA37;` |
| BulletedList | E8FD | `&#xE8FD;` |
| CheckList | E9D5 | `&#xE9D5;` |
| ScrollUpDown | EC8F | `&#xEC8F;` |
| Swipe | E927 | `&#xE927;` |
| Label | E932 | `&#xE932;` |
| Badge | EC1B | `&#xEC1B;` |
| Tooltip | E82F | `&#xE82F;` |
| NotificationBell | F2A3 | `&#xF2A3;` |
| GripBarHorizontal | E76F | `&#xE76F;` |
| GripBarVertical | E784 | `&#xE784;` |

## Date & Time

| Name | Unicode | Glyph |
|------|---------|-------|
| Calendar | E787 | `&#xE787;` |
| CalendarSolid | EA89 | `&#xEA89;` |
| CalendarDay | E8BF | `&#xE8BF;` |
| CalendarWeek | E8C0 | `&#xE8C0;` |
| CalendarReply | E8F5 | `&#xE8F5;` |
| GotoToday | E8D1 | `&#xE8D1;` |
| Clock | E917 | `&#xE917;` |
| Stopwatch | E916 | `&#xE916;` |
| DateTime | EC92 | `&#xEC92;` |
| History | E81C | `&#xE81C;` |
| Recent | E823 | `&#xE823;` |
| Reminder | EB50 | `&#xEB50;` |
| ReminderFill | EB4F | `&#xEB4F;` |
| Snooze | F4BD | `&#xF4BD;` |

## Maps & Location

| Name | Unicode | Glyph |
|------|---------|-------|
| MapPin | E707 | `&#xE707;` |
| MapPin2 | E7B7 | `&#xE7B7;` |
| Location | E81D | `&#xE81D;` |
| MapDirections | E816 | `&#xE816;` |
| MapLayers | E81E | `&#xE81E;` |
| Compass | E812 | `&#xE812;` |
| World | E909 | `&#xE909;` |
| Globe | E774 | `&#xE774;` |
| Directions | E8F0 | `&#xE8F0;` |
| POI | ECAF | `&#xECAF;` |
| Car | E804 | `&#xE804;` |
| Walk | E805 | `&#xE805;` |
| Bus | E806 | `&#xE806;` |
| Train | E7C0 | `&#xE7C0;` |
| Ferry | E7E3 | `&#xE7E3;` |
| ParkingLocation | E811 | `&#xE811;` |

## Fluent-Only Additions (EA+ range, not in MDL2)

These glyphs exist only in `Segoe Fluent Icons` and are not available in `Segoe MDL2 Assets`.

| Name | Unicode | Glyph |
|------|---------|-------|
| VolumeDisabled | EA85 | `&#xEA85;` |
| ChatSparkle | EAB7 | `&#xEAB7;` |
| EmojiEdit | EAAA | `&#xEAAA;` |
| SearchSparkle | ED37 | `&#xED37;` |
| QRCode | ED14 | `&#xED14;` |
| Hide | ED1A | `&#xED1A;` |
| Feedback | ED15 | `&#xED15;` |
| OpenFolderHorizontal | ED25 | `&#xED25;` |
| Apps | ED35 | `&#xED35;` |
| SkipBack10 | ED3C | `&#xED3C;` |
| SkipForward30 | ED3D | `&#xED3D;` |
| Pencil | ED63 | `&#xED63;` |
| Marker | ED64 | `&#xED64;` |
| Ruler | ED5E | `&#xED5E;` |
| CalligraphyPen | EDFB | `&#xEDFB;` |
| Strikethrough | EDE0 | `&#xEDE0;` |
| CloudSearch | EDE4 | `&#xEDE4;` |
| Speech | EFA9 | `&#xEFA9;` |
| HardDrive | EDA2 | `&#xEDA2;` |
| NetworkAdapter | EDA3 | `&#xEDA3;` |
| Touchscreen | EDA4 | `&#xEDA4;` |
| NetworkPrinter | EDA5 | `&#xEDA5;` |
| CloudPrinter | EDA6 | `&#xEDA6;` |
| KeyboardShortcut | EDA7 | `&#xEDA7;` |
| Eyedropper | EF3C | `&#xEF3C;` |
| Replay | EF3B | `&#xEF3B;` |
| PlayerSettings | EF58 | `&#xEF58;` |
| TextEdit | EF60 | `&#xEF60;` |
| Flow | EF90 | `&#xEF90;` |
| Touchpad | EFA5 | `&#xEFA5;` |
| RAM | EEA0 | `&#xEEA0;` |
| CPU | EEA1 | `&#xEEA1;` |
| SummarizeSparkle | EB78 | `&#xEB78;` |
| CloudDownload | EBD3 | `&#xEBD3;` |
| Uninstall | EC91 | `&#xEC91;` |
| FormatText | EC34 | `&#xEC34;` |
| ExploreContent | ECCD | `&#xECC;` |
| ScrollMode | ECE7 | `&#xECE7;` |
| ZoomMode | ECE8 | `&#xECE8;` |
| PanMode | ECE9 | `&#xECE9;` |
| Connected | F0B9 | `&#xF0B9;` |
| GridView | F0E2 | `&#xF0E2;` |
| ClipboardList | F0E3 | `&#xF0E3;` |
| PasteSparkle | F03D | `&#xF03D;` |
| AdvancedPaste | F03E | `&#xF03E;` |
| ZipFolder | F012 | `&#xF012;` |
| Widget | F034 | `&#xF034;` |
| WidgetGear | F035 | `&#xF035;` |
| WidgetAdd | F036 | `&#xF036;` |
| CaretSolidLeft | F08D | `&#xF08D;` |
| CaretSolidDown | F08E | `&#xF08E;` |
| CaretSolidRight | F08F | `&#xF08F;` |
| CaretSolidUp | F090 | `&#xF090;` |
| SignOut | F3B1 | `&#xF3B1;` |
| StatusCheckmark | F1D8 | `&#xF1D8;` |
| StatusInfo | F3CC | `&#xF3CC;` |
| StatusCircleCheckmark | F13E | `&#xF13E;` |
| StatusCircleErrorX | F13D | `&#xF13D;` |
| StatusCircleInfo | F13F | `&#xF13F;` |
| StatusCircleBlock | F140 | `&#xF140;` |
| StatusCircleSync | F143 | `&#xF143;` |
| ToggleLeft | F19E | `&#xF19E;` |
| ToggleRight | F19F | `&#xF19F;` |
| ProgressRingDots | F16A | `&#xF16A;` |
| LikeSolid | F3BF | `&#xF3BF;` |
| DislikeSolid | F3C0 | `&#xF3C0;` |
| NearbySharing | F3E2 | `&#xF3E2;` |
| DeclineCall | F405 | `&#xF405;` |
| CopyTo | F413 | `&#xF413;` |
| PencilFill | F0C6 | `&#xF0C6;` |
| Beaker | F196 | `&#xF196;` |
| ViewDashboard | F246 | `&#xF246;` |
| Restart3 | F305 | `&#xF305;` |
| NetworkOffline | F384 | `&#xF384;` |
| NetworkConnected | F385 | `&#xF385;` |
| NetworkConnectedCheckmark | F386 | `&#xF386;` |
| ScreenTime | F182 | `&#xF182;` |
| BatterySaver | F432 | `&#xF432;` |
| IOT | F22C | `&#xF22C;` |
| Safe | F540 | `&#xF540;` |
