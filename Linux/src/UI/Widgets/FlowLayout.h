/**
 * @file FlowLayout.h
 * @brief Layout с автоматическим переносом элементов
 */

#pragma once

#include <QLayout>
#include <QRect>
#include <QStyle>
#include <QWidgetItem>

namespace BarkFluff {

/**
 * @brief Layout с автоматическим переносом элементов на следующую строку
 * 
 * Используется для отображения бейджей и других элементов,
 * которые должны переноситься при нехватке места.
 */
class FlowLayout : public QLayout {
public:
    explicit FlowLayout(QWidget* parent, int margin = -1, int hSpacing = -1, int vSpacing = -1);
    explicit FlowLayout(int margin = -1, int hSpacing = -1, int vSpacing = -1);
    ~FlowLayout() override;

    void addItem(QLayoutItem* item) override;
    int horizontalSpacing() const;
    int verticalSpacing() const;
    Qt::Orientations expandingDirections() const override;
    bool hasHeightForWidth() const override;
    int heightForWidth(int) const override;
    int count() const override;
    QLayoutItem* itemAt(int index) const override;
    QSize minimumSize() const override;
    void setGeometry(const QRect& rect) override;
    QSize sizeHint() const override;
    QLayoutItem* takeAt(int index) override;

private:
    int doLayout(const QRect& rect, bool testOnly) const;
    int smartSpacing(QStyle::PixelMetric pm) const;

    QList<QLayoutItem*> itemList_;
    int hSpace_ = -1;
    int vSpace_ = -1;
};

} // namespace BarkFluff