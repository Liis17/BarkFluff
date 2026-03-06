/**
 * @file EmojiPicker.h
 * @brief Виджет выбора эмодзи с категориями и поиском
 */

#pragma once

#include <QWidget>
#include <QDialog>
#include <QLineEdit>
#include <QListWidget>
#include <QTabWidget>
#include <QMap>
#include <QStringList>
#include <QSettings>

namespace BarkFluff {

/**
 * @brief Виджет для выбора эмодзи
 * 
 * Содержит категории эмодзи, поиск и недавно использованные.
 * Всплывающий виджет, привязанный к родительскому окну.
 */
class EmojiPicker : public QWidget {
    Q_OBJECT

public:
    explicit EmojiPicker(QWidget* parent = nullptr);
    ~EmojiPicker() override = default;

signals:
    /**
     * @brief Сигнал при выборе эмодзи
     * @param emoji Выбранный эмодзи
     */
    void emojiSelected(const QString& emoji);

protected:
    void showEvent(QShowEvent* event) override;
    void keyPressEvent(QKeyEvent* event) override;
    bool eventFilter(QObject* watched, QEvent* event) override;

private slots:
    void onSearchChanged(const QString& text);
    void onEmojiClicked(QListWidgetItem* item);
    void onCategoryChanged(int index);

private:
    void setupUI();
    void loadEmojis();
    void loadRecentEmojis();
    void saveRecentEmojis();
    void addEmojiToRecent(const QString& emoji);
    void populateCategory(int categoryIndex);
    void populateSearchResults(const QString& query);
    QListWidget* createEmojiList();

    // UI components
    QLineEdit* searchEdit_;
    QTabWidget* categoryTabs_;
    
    // Emoji data
    struct EmojiCategory {
        QString name;           ///< Название категории
        QString icon;           ///< Иконка категории (эмодзи)
        QStringList emojis;     ///< Список эмодзи
        QStringList names;      ///< Названия для поиска
    };
    
    QList<EmojiCategory> categories_;
    QStringList recentEmojis_;
    
    // Constants
    static constexpr int MAX_RECENT = 24;  ///< Макс. кол-во недавних эмодзи
    static constexpr int EMOJI_SIZE = 32;  ///< Размер эмодзи в списке (увеличен)
};

} // namespace BarkFluff