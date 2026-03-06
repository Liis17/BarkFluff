/**
 * @file EmojiPicker.cpp
 * @brief Реализация виджета выбора эмодзи
 */

#include "EmojiPicker.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QKeyEvent>
#include <QDebug>
#include <QPushButton>
#include <QApplication>
#include <QEvent>

namespace BarkFluff {

// Увеличенный размер эмодзи
static constexpr int EMOJI_SIZE_LARGE = 52;

EmojiPicker::EmojiPicker(QWidget* parent)
    : QWidget(parent)
{
    setupUI();
    loadEmojis();
    loadRecentEmojis();
    
    // Install event filter to close on click outside
    qApp->installEventFilter(this);
}

void EmojiPicker::setupUI() {
    setWindowTitle(tr("Выбор эмодзи"));
    setWindowFlags(Qt::Popup);  // Popup window - closes on click outside
    setAttribute(Qt::WA_ShowWithoutActivating, false);
    
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(12, 12, 12, 12);
    mainLayout->setSpacing(8);
    
    // Set background style
    setStyleSheet(
        "EmojiPicker { background: white; border: 1px solid #e0e0e0; border-radius: 8px; }"
    );
    
    // Header with close button
    auto* headerLayout = new QHBoxLayout();
    
    // Search
    searchEdit_ = new QLineEdit(this);
    searchEdit_->setPlaceholderText(tr("Поиск эмодзи..."));
    searchEdit_->setStyleSheet(
        "QLineEdit { padding: 10px; border: 1px solid #e0e0e0; "
        "border-radius: 6px; font-size: 14px; background: white; }"
    );
    connect(searchEdit_, &QLineEdit::textChanged,
            this, &EmojiPicker::onSearchChanged);
    headerLayout->addWidget(searchEdit_, 1);
    
    // Close button
    auto* closeButton = new QPushButton("✕", this);
    closeButton->setFixedSize(36, 36);
    closeButton->setStyleSheet(
        "QPushButton { font-size: 18px; border: none; background: #f0f0f0; "
        "border-radius: 18px; }"
        "QPushButton:hover { background: #e0e0e0; }"
        "QPushButton:pressed { background: #d0d0d0; }"
    );
    connect(closeButton, &QPushButton::clicked, this, &QWidget::hide);
    headerLayout->addWidget(closeButton);
    
    mainLayout->addLayout(headerLayout);
    
    // Category tabs
    categoryTabs_ = new QTabWidget(this);
    categoryTabs_->setStyleSheet(
        "QTabWidget::pane { border: 1px solid #e0e0e0; border-radius: 6px; background: white; }"
        "QTabBar::tab { padding: 10px 16px; font-size: 24px; margin: 2px; }"
        "QTabBar::tab:selected { background: #e3f2fd; border-radius: 4px; }"
        "QTabBar::tab:hover { background: #f0f0f0; border-radius: 4px; }"
    );
    connect(categoryTabs_, &QTabWidget::currentChanged,
            this, &EmojiPicker::onCategoryChanged);
    mainLayout->addWidget(categoryTabs_);
    
    // Set size (larger for bigger emojis)
    setFixedSize(450, 500);
}

void EmojiPicker::loadEmojis() {
    // Смайлики и эмоции - полные ключевые слова (EN + RU)
    EmojiCategory smileys;
    smileys.name = tr("Смайлики");
    smileys.icon = QString::fromUtf8("😀");
    smileys.emojis = {
        QString::fromUtf8("😀"), QString::fromUtf8("😃"), QString::fromUtf8("😄"), QString::fromUtf8("😁"),
        QString::fromUtf8("😅"), QString::fromUtf8("😂"), QString::fromUtf8("🤣"), QString::fromUtf8("😊"),
        QString::fromUtf8("😇"), QString::fromUtf8("🙂"), QString::fromUtf8("🙃"), QString::fromUtf8("😉"),
        QString::fromUtf8("😌"), QString::fromUtf8("😍"), QString::fromUtf8("🥰"), QString::fromUtf8("😘"),
        QString::fromUtf8("😗"), QString::fromUtf8("😙"), QString::fromUtf8("😚"), QString::fromUtf8("😋"),
        QString::fromUtf8("😛"), QString::fromUtf8("😜"), QString::fromUtf8("🤪"), QString::fromUtf8("😝"),
        QString::fromUtf8("🤑"), QString::fromUtf8("🤗"), QString::fromUtf8("🤭"), QString::fromUtf8("🤫"),
        QString::fromUtf8("🤔"), QString::fromUtf8("🤐"), QString::fromUtf8("🤨"), QString::fromUtf8("😐"),
        QString::fromUtf8("😑"), QString::fromUtf8("😶"), QString::fromUtf8("😏"), QString::fromUtf8("😒"),
        QString::fromUtf8("🙄"), QString::fromUtf8("😬"), QString::fromUtf8("🤥"), QString::fromUtf8("😌"),
        QString::fromUtf8("😔"), QString::fromUtf8("😪"), QString::fromUtf8("🤤"), QString::fromUtf8("😴"),
        QString::fromUtf8("😷"), QString::fromUtf8("🤒"), QString::fromUtf8("🤕"), QString::fromUtf8("🤢"),
        QString::fromUtf8("🤮"), QString::fromUtf8("🤧"), QString::fromUtf8("🥵"), QString::fromUtf8("🥶"),
        QString::fromUtf8("🥴"), QString::fromUtf8("😵"), QString::fromUtf8("🤯"), QString::fromUtf8("🤠"),
        QString::fromUtf8("🥳"), QString::fromUtf8("😎"), QString::fromUtf8("🤓"), QString::fromUtf8("🧐"),
        QString::fromUtf8("😕"), QString::fromUtf8("😟"), QString::fromUtf8("🙁"), QString::fromUtf8("☹️"),
        QString::fromUtf8("😮"), QString::fromUtf8("😯"), QString::fromUtf8("😲"), QString::fromUtf8("😳"),
        QString::fromUtf8("🥺"), QString::fromUtf8("😦"), QString::fromUtf8("😧"), QString::fromUtf8("😨"),
        QString::fromUtf8("😰"), QString::fromUtf8("😥"), QString::fromUtf8("😢"), QString::fromUtf8("😭"),
        QString::fromUtf8("😱"), QString::fromUtf8("😖"), QString::fromUtf8("😣"), QString::fromUtf8("😞"),
        QString::fromUtf8("😓"), QString::fromUtf8("😩"), QString::fromUtf8("😫"), QString::fromUtf8("🥱"),
        QString::fromUtf8("😤"), QString::fromUtf8("😡"), QString::fromUtf8("😠"), QString::fromUtf8("🤬"),
        QString::fromUtf8("😈"), QString::fromUtf8("👿"), QString::fromUtf8("💀"), QString::fromUtf8("☠️"),
        QString::fromUtf8("💩"), QString::fromUtf8("🤡"), QString::fromUtf8("👹"), QString::fromUtf8("👺"),
        QString::fromUtf8("👻"), QString::fromUtf8("👽"), QString::fromUtf8("👾"), QString::fromUtf8("🤖"),
    };
    smileys.names = {
        "grin smile улыбка", "smile happy счастливый", "joy laugh радость смех", "beam grin ухмылка",
        "sweat grin пот улыбка", "rofl lol лол рофл", "rofl laugh кривляться", "blush shy стыдливый",
        "innocent angel ангел невинный", "slight smile легкая", "upside перевернутый", "wink подмигивание",
        "relieved calm спокойный", "heart eyes любовь влюбленный", "love adore обожать сердечки", "kiss love поцелуй",
        "kissing губы", "kissing closed", "kissing heart", "yummy вкусно вкусный",
        "tongue язык", "crazy tongue сумасшедший", "zany wild дикий", "tongue squint прищур",
        "money богатый деньги", "hug обнимать", "quiet секрет тише", "shush тихо молчать",
        "think думать мысли", "zip рот закрыт молчи", "eyebrow бровь", "neutral нейтральный",
        "expressionless безразличный", "silent молчание", "smirk ухмылка", "unamused недоволен",
        "roll eyes закатить глаза", "grimace гримаса", "lying ложь врать", "relieved облегчение",
        "pensive грустный задумчивый", "sleepy сонный", "drool слюни", "sleep сон спать",
        "mask маска болезнь", "ill больной", "hurt injured раненый", "sick тошно",
        "vomit рвота блевать", "sneeze чихать", "hot жарко", "cold холодно замерз",
        "drunk пьяный", "dizzy головокружение", "mind blown взрыв мозга", "cowboy ковбой",
        "party праздник", "cool солнце очки", "nerd ботан", "monocle монокль лупа",
        "confused озадачен", "worried обеспокоен", "slight sad грустный", "frowning насупился",
        "surprise удивление", "hushed тихий шок", "astonished изумлен", "flushed смущен",
        "plead просьба", "frown хмурый", "anguished страдающий", "fearful испуганный страх",
        "anxious тревога", "sad disappointed грусть разочарован", "cry плакать рыдать", "sob рыдания плач",
        "scream кричать ужас", "confused страдание", "persevere терпеть", "disappointed разочарование",
        "sweat пот нервничать", "weary усталый измученный", "tired уставший", "yawn зевать устал",
        "triumph победа гордость", "angry злой ярость", "rage гнев бешенство", "swear ругаться мат",
        "devil дьявол зло", "devil angry демон", "skull череп смерть", "skull death умереть",
        "poop какашка дерьмо", "clown клоун", "ogre людоед", "goblin гоблин",
        "ghost призрак дух", "alien пришелец нло", "alien monster монстр", "robot робот"
    };
    categories_.append(smileys);
    
    // Жесты и люди
    EmojiCategory gestures;
    gestures.name = tr("Жесты");
    gestures.icon = QString::fromUtf8("👋");
    gestures.emojis = {
        QString::fromUtf8("👋"), QString::fromUtf8("🤚"), QString::fromUtf8("🖐"), QString::fromUtf8("✋"),
        QString::fromUtf8("🖖"), QString::fromUtf8("👌"), QString::fromUtf8("🤌"), QString::fromUtf8("🤏"),
        QString::fromUtf8("✌️"), QString::fromUtf8("🤞"), QString::fromUtf8("🤟"), QString::fromUtf8("🤘"),
        QString::fromUtf8("🤙"), QString::fromUtf8("👈"), QString::fromUtf8("👉"), QString::fromUtf8("👆"),
        QString::fromUtf8("🖕"), QString::fromUtf8("👇"), QString::fromUtf8("☝️"), QString::fromUtf8("👍"),
        QString::fromUtf8("👎"), QString::fromUtf8("✊"), QString::fromUtf8("👊"), QString::fromUtf8("🤛"),
        QString::fromUtf8("🤜"), QString::fromUtf8("👏"), QString::fromUtf8("🙌"), QString::fromUtf8("👐"),
        QString::fromUtf8("🤲"), QString::fromUtf8("🤝"), QString::fromUtf8("🙏"), QString::fromUtf8("✍️"),
        QString::fromUtf8("💪"), QString::fromUtf8("🦾"), QString::fromUtf8("🦿"), QString::fromUtf8("🦵"),
        QString::fromUtf8("🦶"), QString::fromUtf8("👂"), QString::fromUtf8("🦻"), QString::fromUtf8("👃"),
        QString::fromUtf8("🧠"), QString::fromUtf8("🫀"), QString::fromUtf8("🫁"), QString::fromUtf8("🦷"),
        QString::fromUtf8("🦴"), QString::fromUtf8("👀"), QString::fromUtf8("👁"), QString::fromUtf8("👅"),
        QString::fromUtf8("👄"), QString::fromUtf8("👶"), QString::fromUtf8("🧒"), QString::fromUtf8("👦"),
        QString::fromUtf8("👧"), QString::fromUtf8("🧑"), QString::fromUtf8("👱"), QString::fromUtf8("👨"),
        QString::fromUtf8("👩"), QString::fromUtf8("🧔"), QString::fromUtf8("🧓"), QString::fromUtf8("👴"),
        QString::fromUtf8("👵"), QString::fromUtf8("🙋"), QString::fromUtf8("🙇"), QString::fromUtf8("🤦"),
    };
    gestures.names = {
        "wave привет машу", "raised back", "hand palm ладонь", "stop palm стоп",
        "vulcan spock", "ok good отлично ок", "pinched italian", "pinch little мало",
        "victory peace мир победа", "crossed fingers удача", "love you люблю", "horns rock рок",
        "call phone звони дай пять", "point left влево", "point right вправо", "point up вверх",
        "middle finger фак", "point down вниз", "info up", "thumbs up класс одобряю",
        "thumbs down плохо не одобряю", "fist кулак", "punch удар", "fist left",
        "fist right", "clap хлопать аплодисменты", "hooray ура руки вверх", "open hands открытые",
        "palms together", "handshake рукопожатие", "pray please молясь пожалуйста", "writing пишу",
        "biceps strong мышца сила", "mechanical протез", "prosthetic нога протез", "leg нога",
        "foot стопа", "ear ухо", "ear hearing", "nose нос",
        "brain мозг", "heart сердце", "lungs легкие", "tooth зуб",
        "bone кость", "eyes глаза", "eye глаз", "tongue язык",
        "mouth губы", "baby малыш", "child ребенок", "boy мальчик",
        "girl девочка", "person человек", "blond блондин", "man мужчина",
        "woman женщина", "bearded бородач", "older пожилой", "old man старик дед",
        "old woman бабушка", "raising hand руку", "bow поклон", "facepalm фейспалм"
    };
    categories_.append(gestures);
    
    // Сердца и эмоции
    EmojiCategory hearts;
    hearts.name = tr("Сердца");
    hearts.icon = QString::fromUtf8("❤️");
    hearts.emojis = {
        QString::fromUtf8("❤️"), QString::fromUtf8("🧡"), QString::fromUtf8("💛"), QString::fromUtf8("💚"),
        QString::fromUtf8("💙"), QString::fromUtf8("💜"), QString::fromUtf8("🖤"), QString::fromUtf8("🤍"),
        QString::fromUtf8("🤎"), QString::fromUtf8("💔"), QString::fromUtf8("❣️"), QString::fromUtf8("💕"),
        QString::fromUtf8("💞"), QString::fromUtf8("💓"), QString::fromUtf8("💗"), QString::fromUtf8("💖"),
        QString::fromUtf8("💘"), QString::fromUtf8("💝"), QString::fromUtf8("💟"), QString::fromUtf8("☮️"),
        QString::fromUtf8("✝️"), QString::fromUtf8("☪️"), QString::fromUtf8("🕉"), QString::fromUtf8("☸️"),
        QString::fromUtf8("✡️"), QString::fromUtf8("🔯"), QString::fromUtf8("🕎"), QString::fromUtf8("☯️"),
        QString::fromUtf8("☦️"), QString::fromUtf8("🛐"), QString::fromUtf8("⛎"), QString::fromUtf8("♈"),
        QString::fromUtf8("♉"), QString::fromUtf8("♊"), QString::fromUtf8("♋"), QString::fromUtf8("♌"),
        QString::fromUtf8("♍"), QString::fromUtf8("♎"), QString::fromUtf8("♏"), QString::fromUtf8("♐"),
        QString::fromUtf8("♑"), QString::fromUtf8("♒"), QString::fromUtf8("♓"), QString::fromUtf8("⚛️"),
    };
    hearts.names = {
        "red heart любовь сердце красное", "orange heart оранжевое", "yellow heart желтое", "green heart зеленое",
        "blue heart синее голубое", "purple heart фиолетовое", "black heart черное", "white heart белое",
        "brown heart коричневое", "broken heart разбитое грусть", "exclamation восклицание", "two hearts два",
        "revolving hearts", "beating сердце бьется", "growing heart растет", "sparkle блеск",
        "cupid купидон стрелы", "gift heart подарок", "heart decoration", "peace мир",
        "cross крест", "islam ислам полумесяц", "hindu индуизм", "buddhism буддизм",
        "jewish иудаизм звезда", "dotted шесть", "menorah", "yin yang инь ян",
        "orthodox православие", "pray worship молитва", "ophiuchus змееносец", "aries овен",
        "taurus телец", "gemini близнецы", "cancer рак", "leo лев",
        "virgo дева", "libra весы", "scorpio скорпион", "sagittarius стрелец",
        "capricorn козерог", "aquarius водолей", "pisces рыбы", "atom атом наука"
    };
    categories_.append(hearts);
    
    // Животные
    EmojiCategory animals;
    animals.name = tr("Животные");
    animals.icon = QString::fromUtf8("🐶");
    animals.emojis = {
        QString::fromUtf8("🐶"), QString::fromUtf8("🐱"), QString::fromUtf8("🐭"), QString::fromUtf8("🐹"),
        QString::fromUtf8("🐰"), QString::fromUtf8("🦊"), QString::fromUtf8("🐻"), QString::fromUtf8("🐼"),
        QString::fromUtf8("🐨"), QString::fromUtf8("🐯"), QString::fromUtf8("🦁"), QString::fromUtf8("🐮"),
        QString::fromUtf8("🐷"), QString::fromUtf8("🐸"), QString::fromUtf8("🐵"), QString::fromUtf8("🙈"),
        QString::fromUtf8("🙉"), QString::fromUtf8("🙊"), QString::fromUtf8("🐒"), QString::fromUtf8("🐔"),
        QString::fromUtf8("🐧"), QString::fromUtf8("🐦"), QString::fromUtf8("🐤"), QString::fromUtf8("🦆"),
        QString::fromUtf8("🦅"), QString::fromUtf8("🦉"), QString::fromUtf8("🦇"), QString::fromUtf8("🐺"),
        QString::fromUtf8("🐗"), QString::fromUtf8("🐴"), QString::fromUtf8("🦄"), QString::fromUtf8("🐝"),
        QString::fromUtf8("🐛"), QString::fromUtf8("🦋"), QString::fromUtf8("🐌"), QString::fromUtf8("🐞"),
        QString::fromUtf8("🐜"), QString::fromUtf8("🦟"), QString::fromUtf8("🦗"), QString::fromUtf8("🕷"),
        QString::fromUtf8("🐢"), QString::fromUtf8("🐍"), QString::fromUtf8("🦎"), QString::fromUtf8("🦖"),
        QString::fromUtf8("🐙"), QString::fromUtf8("🦑"), QString::fromUtf8("🦐"), QString::fromUtf8("🦞"),
        QString::fromUtf8("🦀"), QString::fromUtf8("🐡"), QString::fromUtf8("🐠"), QString::fromUtf8("🐟"),
        QString::fromUtf8("🐬"), QString::fromUtf8("🐳"), QString::fromUtf8("🐋"), QString::fromUtf8("🦈"),
        QString::fromUtf8("🐊"), QString::fromUtf8("🐅"), QString::fromUtf8("🐆"), QString::fromUtf8("🦓"),
    };
    animals.names = {
        "dog собака пес славик", "cat кошка кот", "mouse мышь", "hamster хомяк",
        "rabbit bunny кролик заяц", "fox лиса", "bear медведь", "panda панда",
        "koala коала", "tiger тигр", "lion лев", "cow корова бык",
        "pig свинья поросенок", "frog лягушка жаба", "monkey monkey face обезьяна", "see no evil не вижу",
        "hear no evil не слышу", "speak no evil не говорю", "monkey обезьяна", "chicken курица петух",
        "penguin пингвин", "bird птица", "chick цыпленок", "duck утка",
        "eagle орел", "owl сова филин", "bat летучая мышь", "wolf волк",
        "boar кабан", "horse лошадь конь", "unicorn единорог", "bee пчела",
        "bug червяк жук", "butterfly бабочка", "snail улитка", "ladybug божья коровка",
        "ant муравей", "mosquito комар", "cricket сверчок", "spider паук",
        "turtle черепаха", "snake змея", "lizard ящерица", "t-rex динозавр тираннозавр",
        "octopus осьминог", "squid кальмар", "shrimp креветка", "lobster омар лобстер",
        "crab краб", "blowfish рыба шар иглобрюх", "tropical fish рыбка", "fish рыба",
        "dolphin дельфин", "whale кит", "whale 2 кит", "shark акула",
        "crocodile крокодил аллигатор", "tiger тигр", "leopard леопард", "zebra зебра"
    };
    categories_.append(animals);
    
    // Еда
    EmojiCategory food;
    food.name = tr("Еда");
    food.icon = QString::fromUtf8("🍕");
    food.emojis = {
        QString::fromUtf8("🍎"), QString::fromUtf8("🍐"), QString::fromUtf8("🍊"), QString::fromUtf8("🍋"),
        QString::fromUtf8("🍌"), QString::fromUtf8("🍉"), QString::fromUtf8("🍇"), QString::fromUtf8("🍓"),
        QString::fromUtf8("🫐"), QString::fromUtf8("🍈"), QString::fromUtf8("🍒"), QString::fromUtf8("🍑"),
        QString::fromUtf8("🥭"), QString::fromUtf8("🍍"), QString::fromUtf8("🥥"), QString::fromUtf8("🥝"),
        QString::fromUtf8("🍅"), QString::fromUtf8("🍆"), QString::fromUtf8("🥑"), QString::fromUtf8("🥦"),
        QString::fromUtf8("🥬"), QString::fromUtf8("🥒"), QString::fromUtf8("🌶"), QString::fromUtf8("🫑"),
        QString::fromUtf8("🌽"), QString::fromUtf8("🥕"), QString::fromUtf8("🧄"), QString::fromUtf8("🧅"),
        QString::fromUtf8("🥔"), QString::fromUtf8("🍠"), QString::fromUtf8("🥐"), QString::fromUtf8("🥯"),
        QString::fromUtf8("🍞"), QString::fromUtf8("🥖"), QString::fromUtf8("🥨"), QString::fromUtf8("🧀"),
        QString::fromUtf8("🥚"), QString::fromUtf8("🍳"), QString::fromUtf8("🧈"), QString::fromUtf8("🥞"),
        QString::fromUtf8("🧇"), QString::fromUtf8("🥓"), QString::fromUtf8("🥩"), QString::fromUtf8("🍗"),
        QString::fromUtf8("🍖"), QString::fromUtf8("🌭"), QString::fromUtf8("🍔"), QString::fromUtf8("🍟"),
        QString::fromUtf8("🍕"), QString::fromUtf8("🥪"), QString::fromUtf8("🥙"), QString::fromUtf8("🧆"),
        QString::fromUtf8("🌮"), QString::fromUtf8("🌯"), QString::fromUtf8("🥗"), QString::fromUtf8("🥘"),
        QString::fromUtf8("🍝"), QString::fromUtf8("🍜"), QString::fromUtf8("🍲"), QString::fromUtf8("🍛"),
        QString::fromUtf8("🍣"), QString::fromUtf8("🍱"), QString::fromUtf8("🥟"), QString::fromUtf8("🦪"),
    };
    food.names = {
        "apple яблоко", "pear груша", "tangerine мандарин апельсин", "lemon лимон",
        "banana банан", "watermelon арбуз", "grapes виноград", "strawberry клубника",
        "blueberries черника голубика", "melon дыня", "cherries вишня черешня", "peach персик",
        "mango манго", "pineapple ананас", "coconut кокос", "kiwi киви",
        "tomato томат помидор", "eggplant cock баклажан член писька", "avocado авокадо", "broccoli брокколи",
        "lettuce салат", "cucumber огурец", "pepper перец", "bell pepper болгарский",
        "corn кукуруза", "carrot морковь", "garlic чеснок", "onion лук",
        "potato картошка картофель", "sweet potato батат", "croissant круассан", "bagel бейгл",
        "bread хлеб", "baquette багет", "pretzel крендель", "cheese сыр",
        "egg яйцо", "cooking жарить яйцо", "butter масло", "pancakes блины оладьи",
        "waffle вафля", "bacon бекон", "meat мясо стейк", "poultry курица ножка",
        "meat bone кость", "hot dog сосиска хотдог", "hamburger бургер гамбургер", "fries картошка фри",
        "pizza пицца", "sandwich сэндвич бутерброд", "stuffed pit", "falafel фалафель",
        "taco тако", "burrito буррито", "green salad салат", "shallow pan pan paella",
        "spaghetti паста макароны", "steaming ramen рамен лапша", "pot of food суп", "curry карри рис",
        "sushi суши", "bento box бенто", "dumpling пельмени", "oyster устрица"
    };
    categories_.append(food);
    
    // Природа и погода
    EmojiCategory nature;
    nature.name = tr("Природа");
    nature.icon = QString::fromUtf8("🌸");
    nature.emojis = {
        QString::fromUtf8("🌸"), QString::fromUtf8("💮"), QString::fromUtf8("🏵"), QString::fromUtf8("🌹"),
        QString::fromUtf8("🥀"), QString::fromUtf8("🌺"), QString::fromUtf8("🌻"), QString::fromUtf8("🌼"),
        QString::fromUtf8("🌷"), QString::fromUtf8("🌱"), QString::fromUtf8("🪴"), QString::fromUtf8("🌲"),
        QString::fromUtf8("🌳"), QString::fromUtf8("🌴"), QString::fromUtf8("🌵"), QString::fromUtf8("🌾"),
        QString::fromUtf8("🌿"), QString::fromUtf8("☘️"), QString::fromUtf8("🍀"), QString::fromUtf8("🍁"),
        QString::fromUtf8("🍂"), QString::fromUtf8("🍃"), QString::fromUtf8("🌑"), QString::fromUtf8("🌒"),
        QString::fromUtf8("🌓"), QString::fromUtf8("🌔"), QString::fromUtf8("🌕"), QString::fromUtf8("🌖"),
        QString::fromUtf8("🌗"), QString::fromUtf8("🌘"), QString::fromUtf8("🌙"), QString::fromUtf8("🌚"),
        QString::fromUtf8("🌛"), QString::fromUtf8("🌜"), QString::fromUtf8("🌡"), QString::fromUtf8("☀️"),
        QString::fromUtf8("🌝"), QString::fromUtf8("🌞"), QString::fromUtf8("⭐"), QString::fromUtf8("🌟"),
        QString::fromUtf8("🌠"), QString::fromUtf8("☁️"), QString::fromUtf8("⛅"), QString::fromUtf8("⛈"),
        QString::fromUtf8("🌤"), QString::fromUtf8("🌥"), QString::fromUtf8("🌦"), QString::fromUtf8("🌧"),
        QString::fromUtf8("🌨"), QString::fromUtf8("❄️"), QString::fromUtf8("☃️"), QString::fromUtf8("⛄"),
        QString::fromUtf8("🌬"), QString::fromUtf8("💨"), QString::fromUtf8("🌪"), QString::fromUtf8("🌫"),
        QString::fromUtf8("🌈"), QString::fromUtf8("🌊"), QString::fromUtf8("💧"), QString::fromUtf8("🔥"),
    };
    nature.names = {
        "cherry blossom сакура цветок", "white flower", "rosette розетка", "rose роза",
        "wilted flower увядший цветок", "hibiscus гибискус", "sunflower подсолнух", "blossom цветок",
        "tulip тюльпан", "seedling росток", "potted plant горшок растение", "evergreen tree ель",
        "deciduous tree дерево", "palm tree пальма", "cactus кактус", "sheaf of rice рис",
        "herb трава", "shamrock трилистник", "four leaf clover клевер", "maple leaf клен лист",
        "fallen leaf опавший лист", "leaf fluttering лист", "new moon новолуние", "waxing crescent",
        "first quarter", "waxing gibbous", "full moon полнолуние", "waning gibbous",
        "last quarter", "waning crescent", "crescent moon месяц", "new moon face",
        "first quarter", "last quarter", "thermometer градусник температура", "sun солнце",
        "full moon face", "sun with face", "star звезда", "glowing star звезда",
        "shooting star падающая звезда", "cloud облако", "sun behind cloud солнце за облаком", "cloud lightning дождь гроза",
        "sun behind small cloud", "sun behind large cloud", "sun behind rain cloud", "cloud with rain дождь",
        "cloud with snow снег", "snowflake снежинка", "snowman снеговик", "snowman without snow",
        "wind face ветер", "dash of air", "tornado торнадо", "fog туман",
        "rainbow радуга", "water wave волна", "droplet капля", "fire огонь пламя"
    };
    categories_.append(nature);
    
    // Предметы
    EmojiCategory objects;
    objects.name = tr("Предметы");
    objects.icon = QString::fromUtf8("💡");
    objects.emojis = {
        QString::fromUtf8("⌚"), QString::fromUtf8("📱"), QString::fromUtf8("💻"), QString::fromUtf8("⌨️"),
        QString::fromUtf8("🖥"), QString::fromUtf8("🖨"), QString::fromUtf8("🖱"), QString::fromUtf8("🖲"),
        QString::fromUtf8("🕹"), QString::fromUtf8("🗜"), QString::fromUtf8("💽"), QString::fromUtf8("💾"),
        QString::fromUtf8("💿"), QString::fromUtf8("📀"), QString::fromUtf8("📼"), QString::fromUtf8("📷"),
        QString::fromUtf8("📸"), QString::fromUtf8("📹"), QString::fromUtf8("🎥"), QString::fromUtf8("📽"),
        QString::fromUtf8("🎞"), QString::fromUtf8("📞"), QString::fromUtf8("☎️"), QString::fromUtf8("📟"),
        QString::fromUtf8("📠"), QString::fromUtf8("📺"), QString::fromUtf8("📻"), QString::fromUtf8("🎙"),
        QString::fromUtf8("🎚"), QString::fromUtf8("🎛"), QString::fromUtf8("🧭"), QString::fromUtf8("⏱"),
        QString::fromUtf8("⏲"), QString::fromUtf8("⏰"), QString::fromUtf8("🕰"), QString::fromUtf8("⌛"),
        QString::fromUtf8("⏳"), QString::fromUtf8("📡"), QString::fromUtf8("🔋"), QString::fromUtf8("🔌"),
        QString::fromUtf8("💡"), QString::fromUtf8("🔦"), QString::fromUtf8("🕯"), QString::fromUtf8("🪔"),
        QString::fromUtf8("🧯"), QString::fromUtf8("🛢"), QString::fromUtf8("💸"), QString::fromUtf8("💵"),
        QString::fromUtf8("💴"), QString::fromUtf8("💶"), QString::fromUtf8("💷"), QString::fromUtf8("💰"),
        QString::fromUtf8("💳"), QString::fromUtf8("💎"), QString::fromUtf8("⚖️"), QString::fromUtf8("🔧"),
        QString::fromUtf8("🔨"), QString::fromUtf8("⚒"), QString::fromUtf8("🛠"), QString::fromUtf8("⛏"),
    };
    objects.names = {
        "watch часы наручные", "mobile phone телефон смартфон", "laptop ноутбук", "keyboard клавиатура",
        "desktop computer компьютер", "printer принтер", "mouse мышь", "trackball",
        "joystick джойстик", "clamp зажим", "computer disk минидиск", "floppy disk дискета",
        "optical disk диск", "dvd dvd диск", "videocassette видеокассета", "camera фотоаппарат",
        "camera flash", "video camera видеокамера", "movie camera", "film projector",
        "film frames", "telephone телефон трубка", "telephone receiver", "pager пейджер",
        "fax факс", "television телевизор", "radio радио", "microphone микрофон",
        "level slider", "control knobs", "compass компас", "stopwatch секундомер",
        "timer таймер", "alarm clock будильник", "mantelpiece clock часы", "hourglass песочные часы",
        "hourglass not done", "satellite antenna спутник", "battery батарея заряд", "plug вилка розетка",
        "light bulb лампочка идея", "flashlight фонарик", "candle свеча", "diya lamp",
        "fire extinguisher огнетушитель", "oil drum бочка", "money fly деньги", "dollar доллар",
        "yen иена", "euro евро", "pound фунт", "money bag мешок денег",
        "credit card карта", "gem diamond бриллиант", "balance весы", "wrench гаечный ключ",
        "hammer молоток", "hammer pick кирка", "hammer wrench инструменты", "pick шахта"
    };
    categories_.append(objects);
    
    // Символы
    EmojiCategory symbols;
    symbols.name = tr("Символы");
    symbols.icon = QString::fromUtf8("⚠️");
    symbols.emojis = {
        QString::fromUtf8("⚠️"), QString::fromUtf8("🚸"), QString::fromUtf8("⛔"), QString::fromUtf8("🚫"),
        QString::fromUtf8("🚳"), QString::fromUtf8("🚭"), QString::fromUtf8("🚯"), QString::fromUtf8("🚱"),
        QString::fromUtf8("🚷"), QString::fromUtf8("📵"), QString::fromUtf8("🔞"), QString::fromUtf8("☢️"),
        QString::fromUtf8("☣️"), QString::fromUtf8("🔱"), QString::fromUtf8("🔰"), QString::fromUtf8("⭕"),
        QString::fromUtf8("✅"), QString::fromUtf8("☑️"), QString::fromUtf8("✔️"), QString::fromUtf8("✖️"),
        QString::fromUtf8("❌"), QString::fromUtf8("❎"), QString::fromUtf8("➕"), QString::fromUtf8("➖"),
        QString::fromUtf8("➗"), QString::fromUtf8("➰"), QString::fromUtf8("➿"), QString::fromUtf8("〽️"),
        QString::fromUtf8("✳️"), QString::fromUtf8("✴️"), QString::fromUtf8("❇️"), QString::fromUtf8("‼️"),
        QString::fromUtf8("⁉️"), QString::fromUtf8("❓"), QString::fromUtf8("❔"), QString::fromUtf8("❕"),
        QString::fromUtf8("❗"), QString::fromUtf8("〰"), QString::fromUtf8("©️"), QString::fromUtf8("®️"),
        QString::fromUtf8("™️"), QString::fromUtf8("#️⃣"), QString::fromUtf8("*️⃣"), QString::fromUtf8("0️⃣"),
        QString::fromUtf8("1️⃣"), QString::fromUtf8("2️⃣"), QString::fromUtf8("3️⃣"), QString::fromUtf8("4️⃣"),
        QString::fromUtf8("5️⃣"), QString::fromUtf8("6️⃣"), QString::fromUtf8("7️⃣"), QString::fromUtf8("8️⃣"),
        QString::fromUtf8("9️⃣"), QString::fromUtf8("🔟"), QString::fromUtf8("🔠"), QString::fromUtf8("🔢"),
    };
    symbols.names = {
        "warning предупреждение осторожно", "children crossing дети", "no entry вход запрещен", "prohibited запрещено",
        "no bicycles велосипед", "no smoking не курить", "no littering не мусорить", "non drinking water",
        "no pedestrians", "no mobile phones без телефона", "no one under 18 18+", "radioactive радиация",
        "biohazard опасность", "trident эмблема", "Japanese symbol новичок", "hollow red circle круг",
        "check галочка", "checkbox checked", "check mark галочка", "multiply умножить",
        "cross крестик нет", "cross mark кнопка", "plus плюс", "minus минус",
        "divide деление", "curly loop", "double curly loop", "part alternation",
        "asterisk звездочка", "eight pointed star", "sparkle блеск", "bangbang",
        "interrobang вопрос восклицание", "question вопрос", "white question", "white exclamation",
        "exclamation восклицание", "wavy dash", "copyright копирайт", "registered зарегистрирован",
        "trade mark торговая марка", "hash key решетка", "keycap asterisk", "zero ноль",
        "one один", "two два", "three три", "four четыре",
        "five пять", "six шесть", "seven семь", "eight восемь",
        "nine девять", "ten десять", "input latin uppercase", "input numbers"
    };
    categories_.append(symbols);
    
    // Флаги
    EmojiCategory flags;
    flags.name = tr("Флаги");
    flags.icon = QString::fromUtf8("🏁");
    flags.emojis = {
        QString::fromUtf8("🏁"), QString::fromUtf8("🚩"), QString::fromUtf8("🎌"), QString::fromUtf8("🏴"),
        QString::fromUtf8("🏳️"), QString::fromUtf8("🏳️‍🌈"), QString::fromUtf8("🏳️‍⚧️"), QString::fromUtf8("🏴‍☠️"),
        QString::fromUtf8("🇷🇺"), QString::fromUtf8("🇺🇸"), QString::fromUtf8("🇬🇧"), QString::fromUtf8("🇩🇪"),
        QString::fromUtf8("🇫🇷"), QString::fromUtf8("🇮🇹"), QString::fromUtf8("🇪🇸"), QString::fromUtf8("🇵🇹"),
        QString::fromUtf8("🇵🇱"), QString::fromUtf8("🇺🇦"), QString::fromUtf8("🇧🇾"), QString::fromUtf8("🇰🇿"),
        QString::fromUtf8("🇨🇳"), QString::fromUtf8("🇯🇵"), QString::fromUtf8("🇰🇷"), QString::fromUtf8("🇮🇳"),
        QString::fromUtf8("🇧🇷"), QString::fromUtf8("🇦🇷"), QString::fromUtf8("🇲🇽"), QString::fromUtf8("🇨🇦"),
        QString::fromUtf8("🇦🇺"), QString::fromUtf8("🇳🇿"), QString::fromUtf8("🇿🇦"), QString::fromUtf8("🇪🇬"),
        QString::fromUtf8("🇮🇱"), QString::fromUtf8("🇹🇷"), QString::fromUtf8("🇸🇦"), QString::fromUtf8("🇦🇪"),
        QString::fromUtf8("🇨🇭"), QString::fromUtf8("🇳🇴"), QString::fromUtf8("🇸🇪"), QString::fromUtf8("🇫🇮"),
    };
    flags.names = {
        "chequered flag финиш гонка", "triangular flag", "crossed flags флаги япония", "black flag черный",
        "white flag белый", "rainbow flag радуга лгбт", "transgender flag трансгендер", "pirate flag пират",
        "russia россия русский", "usa usa америка сша", "uk uk великобритания", "germany германия немецкий",
        "france франция французский", "italy италия итальянский", "spain испания испанский", "portugal португалия",
        "poland польша польский", "ukraine украина украинский зрада", "belarus беларусь", "kazakhstan казахстан",
        "china china китай", "japan japan япония", "korea korea корея", "india индия",
        "brazil бразилия", "argentina аргентина", "mexico мексика", "canada канада",
        "australia австралия", "new zealia новозеландия", "south africa юар африка", "egypt египет",
        "israel израиль", "turkey турция", "saudi arabia саудовская", "uae эмираты дубай",
        "switzerland швейцария", "norway норвегия", "sweden швеция", "finland финляндия"
    };
    categories_.append(flags);
    
    // Create tabs
    for (int i = 0; i < categories_.size(); ++i) {
        auto* list = createEmojiList();
        categoryTabs_->addTab(list, categories_[i].icon);
    }
    
    // Add "Recent" tab at the beginning
    auto* recentList = createEmojiList();
    categoryTabs_->insertTab(0, recentList, QString::fromUtf8("🕐"));
}

QListWidget* EmojiPicker::createEmojiList() {
    auto* list = new QListWidget(this);
    list->setViewMode(QListView::IconMode);
    list->setIconSize(QSize(EMOJI_SIZE_LARGE, EMOJI_SIZE_LARGE));
    list->setSpacing(4);
    list->setResizeMode(QListView::Adjust);
    list->setMovement(QListView::Static);
    list->setFlow(QListView::LeftToRight);
    list->setWrapping(true);
    list->setStyleSheet(
        "QListWidget { background: transparent; border: none; font-family: 'Noto Color Emoji', 'Segoe UI Emoji', 'Apple Color Emoji', sans-serif; }"
        "QListWidget::item { padding: 4px; border-radius: 8px; font-size: 48px; min-width: 56px; min-height: 56px; }"
        "QListWidget::item:hover { background: #e3f2fd; }"
        "QListWidget::item:selected { background: #bbdefb; }"
    );
    
    connect(list, &QListWidget::itemClicked,
            this, &EmojiPicker::onEmojiClicked);
    
    return list;
}

void EmojiPicker::loadRecentEmojis() {
    QSettings settings("BarkFluff", "BarkFluffQt");
    recentEmojis_ = settings.value("emoji/recent").toStringList();
}

void EmojiPicker::saveRecentEmojis() {
    QSettings settings("BarkFluff", "BarkFluffQt");
    settings.setValue("emoji/recent", recentEmojis_);
}

void EmojiPicker::addEmojiToRecent(const QString& emoji) {
    recentEmojis_.removeAll(emoji);
    recentEmojis_.prepend(emoji);
    
    while (recentEmojis_.size() > MAX_RECENT) {
        recentEmojis_.removeLast();
    }
    
    saveRecentEmojis();
}

void EmojiPicker::populateCategory(int categoryIndex) {
    if (categoryIndex < 0 || categoryIndex >= categoryTabs_->count()) {
        return;
    }
    
    auto* list = qobject_cast<QListWidget*>(categoryTabs_->widget(categoryIndex));
    if (!list) return;
    
    list->clear();
    
    // Create large font for emojis
    QFont emojiFont;
    emojiFont.setPointSize(32);  // Large font for emojis
    emojiFont.setFamily("Noto Color Emoji");
    
    // Category 0 is "Recent"
    if (categoryIndex == 0) {
        for (const QString& emoji : recentEmojis_) {
            auto* item = new QListWidgetItem(emoji, list);
            item->setSizeHint(QSize(64, 64));  // Large cell size
            item->setTextAlignment(Qt::AlignCenter);
            item->setFont(emojiFont);  // Set font directly on item
            list->addItem(item);
        }
        
        if (recentEmojis_.isEmpty()) {
            auto* item = new QListWidgetItem(tr("Нет недавних эмодзи"), list);
            item->setFlags(item->flags() & ~Qt::ItemIsSelectable & ~Qt::ItemIsEnabled);
            list->addItem(item);
        }
    } else {
        // Regular categories (offset by 1 due to "Recent" tab)
        int catIdx = categoryIndex - 1;
        if (catIdx >= 0 && catIdx < categories_.size()) {
            for (const QString& emoji : categories_[catIdx].emojis) {
                auto* item = new QListWidgetItem(emoji, list);
                item->setSizeHint(QSize(64, 64));  // Large cell size
                item->setTextAlignment(Qt::AlignCenter);
                item->setFont(emojiFont);  // Set font directly on item
                list->addItem(item);
            }
        }
    }
}

void EmojiPicker::populateSearchResults(const QString& query) {
    if (query.isEmpty()) {
        // Restore normal category view
        onCategoryChanged(categoryTabs_->currentIndex());
        return;
    }
    
    auto* list = qobject_cast<QListWidget*>(categoryTabs_->currentWidget());
    if (!list) return;
    
    list->clear();
    
    // Create large font for emojis
    QFont emojiFont;
    emojiFont.setPointSize(32);
    emojiFont.setFamily("Noto Color Emoji");
    
    QString lowerQuery = query.toLower();
    QSet<QString> addedEmojis;  // To avoid duplicates
    
    // Search in all categories by names (if available)
    for (const auto& category : categories_) {
        for (int i = 0; i < category.emojis.size(); ++i) {
            const QString& emoji = category.emojis[i];
            
            // Check by name if names array has this index
            bool nameMatches = false;
            if (i < category.names.size()) {
                if (category.names[i].contains(lowerQuery, Qt::CaseInsensitive)) {
                    nameMatches = true;
                }
            }
            
            // Also check if emoji itself contains the query
            bool emojiMatches = emoji.contains(query);
            
            if ((nameMatches || emojiMatches) && !addedEmojis.contains(emoji)) {
                auto* item = new QListWidgetItem(emoji, list);
                item->setSizeHint(QSize(64, 64));
                item->setTextAlignment(Qt::AlignCenter);
                item->setFont(emojiFont);
                list->addItem(item);
                addedEmojis.insert(emoji);
            }
        }
    }
}

void EmojiPicker::showEvent(QShowEvent* event) {
    QWidget::showEvent(event);
    
    // Focus search
    searchEdit_->clear();
    searchEdit_->setFocus();
    
    // Populate current category
    onCategoryChanged(categoryTabs_->currentIndex());
}

void EmojiPicker::keyPressEvent(QKeyEvent* event) {
    if (event->key() == Qt::Key_Escape) {
        hide();
        return;
    }
    
    QWidget::keyPressEvent(event);
}

bool EmojiPicker::eventFilter(QObject* watched, QEvent* event) {
    if (event->type() == QEvent::MouseButtonPress) {
        // Check if click is outside the picker
        if (isVisible() && !rect().contains(mapFromGlobal(QCursor::pos()))) {
            // Don't close if clicking within parent window
            QWidget* w = qobject_cast<QWidget*>(watched);
            if (w && w->window() != window()) {
                hide();
            }
        }
    }
    return QWidget::eventFilter(watched, event);
}

void EmojiPicker::onSearchChanged(const QString& text) {
    populateSearchResults(text);
}

void EmojiPicker::onEmojiClicked(QListWidgetItem* item) {
    QString emoji = item->text();
    
    if (emoji.isEmpty() || emoji == tr("Нет недавних эмодзи")) {
        return;
    }
    
    addEmojiToRecent(emoji);
    emit emojiSelected(emoji);
    // Don't close - allow multiple emoji selection
}

void EmojiPicker::onCategoryChanged(int index) {
    populateCategory(index);
}

} // namespace BarkFluff