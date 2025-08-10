#pragma once
#include <QWidget>
#include <QDate>
#include <memory>

QT_BEGIN_NAMESPACE
class QTableWidget;
class QComboBox;
class QDateEdit;
class QPushButton;
class QScrollArea;
class QHBoxLayout;
class QVBoxLayout;
class QLabel;
class QSpinBox;
QT_END_NAMESPACE

class ResourceCalendarWidget;

class ResourcesPage : public QWidget {
    Q_OBJECT
public:
    explicit ResourcesPage(QWidget* parent = nullptr);
private:
    void setupUI();
    void loadResources();
    void loadProjects();
    void addResource();
    void addProject();
    void updateCalendar();
    void navigatePrevious();
    void navigateNext();
    void navigateToday();
    void changeViewMode();
    
    enum ViewMode { Day, Week, Month };
    ViewMode currentViewMode = Week;
    QDate currentDate;
    
    QVBoxLayout* mainLayout;
    QHBoxLayout* headerLayout;
    QHBoxLayout* contentLayout;
    QVBoxLayout* sidebarLayout;
    
    QPushButton* btnPrevious;
    QPushButton* btnNext;
    QPushButton* btnToday;
    QLabel* lblDateRange;
    QComboBox* cmbViewMode;
    
    QPushButton* btnAddResource;
    QPushButton* btnAddProject;
    QTableWidget* projectsList;
    
    ResourceCalendarWidget* calendarWidget;
    QScrollArea* scrollArea;
};

class ResourceCalendarWidget : public QWidget {
    Q_OBJECT
public:
    explicit ResourceCalendarWidget(QWidget* parent = nullptr);
    void setViewMode(int mode);
    void setCurrentDate(const QDate& date);
    void updateDisplay();
    
protected:
    void paintEvent(QPaintEvent* event) override;
    void mousePressEvent(QMouseEvent* event) override;
    void mouseMoveEvent(QMouseEvent* event) override;
    void mouseReleaseEvent(QMouseEvent* event) override;
    void dragEnterEvent(QDragEnterEvent* event) override;
    void dragMoveEvent(QDragMoveEvent* event) override;
    void dropEvent(QDropEvent* event) override;
    
private:
    struct Resource {
        int id;
        QString name;
        QString role;
        double availability;
    };
    
    struct Project {
        int id;
        QString projectNumber;
        QString name;
        QColor color;
    };
    
    struct Allocation {
        int resourceId;
        int projectId;
        QDate date;
        double hours;
    };
    
    int viewMode;
    QDate currentDate;
    QList<Resource> resources;
    QList<Project> projects;
    QList<Allocation> allocations;
    
    int cellWidth;
    int cellHeight;
    int headerHeight;
    int sidebarWidth;
    
    QPoint dragStartPosition;
    Allocation* draggedAllocation;
    
    void drawHeader(QPainter& painter);
    void drawSidebar(QPainter& painter);
    void drawGrid(QPainter& painter);
    void drawAllocations(QPainter& painter);
    QRect getCellRect(int row, int col);
    int getResourceAtPos(const QPoint& pos);
    QDate getDateAtPos(const QPoint& pos);
};