import AppKit
import SwiftUI

/// A macOS split view whose divider is placed at a requested fraction once,
/// after the window has its real size. NSSplitViewController keeps the divider
/// draggable after that initial placement.
struct InitialRatioSplitView<Leading: View, Trailing: View>: NSViewControllerRepresentable {
    var leadingFraction: CGFloat
    var minimumLeadingWidth: CGFloat
    var minimumTrailingWidth: CGFloat
    private let leading: Leading
    private let trailing: Trailing

    init(
        leadingFraction: CGFloat,
        minimumLeadingWidth: CGFloat,
        minimumTrailingWidth: CGFloat,
        @ViewBuilder leading: () -> Leading,
        @ViewBuilder trailing: () -> Trailing
    ) {
        self.leadingFraction = leadingFraction
        self.minimumLeadingWidth = minimumLeadingWidth
        self.minimumTrailingWidth = minimumTrailingWidth
        self.leading = leading()
        self.trailing = trailing()
    }

    final class Coordinator {
        var leadingController: NSHostingController<Leading>?
        var trailingController: NSHostingController<Trailing>?
    }

    func makeCoordinator() -> Coordinator {
        Coordinator()
    }

    func makeNSViewController(context: Context) -> InitialRatioSplitViewController {
        let splitController = InitialRatioSplitViewController(
            initialLeadingFraction: leadingFraction
        )
        splitController.splitView.isVertical = true
        splitController.splitView.dividerStyle = .thin

        let leadingController = NSHostingController(rootView: leading)
        let leadingItem = NSSplitViewItem(viewController: leadingController)
        leadingItem.minimumThickness = minimumLeadingWidth
        leadingItem.preferredThicknessFraction = leadingFraction
        leadingItem.canCollapse = false

        let trailingController = NSHostingController(rootView: trailing)
        let trailingItem = NSSplitViewItem(viewController: trailingController)
        trailingItem.minimumThickness = minimumTrailingWidth
        trailingItem.preferredThicknessFraction = 1 - leadingFraction
        trailingItem.canCollapse = false

        splitController.addSplitViewItem(leadingItem)
        splitController.addSplitViewItem(trailingItem)
        context.coordinator.leadingController = leadingController
        context.coordinator.trailingController = trailingController
        return splitController
    }

    func updateNSViewController(
        _ nsViewController: InitialRatioSplitViewController,
        context: Context
    ) {
        context.coordinator.leadingController?.rootView = leading
        context.coordinator.trailingController?.rootView = trailing
    }
}

final class InitialRatioSplitViewController: NSSplitViewController {
    private let initialLeadingFraction: CGFloat
    private var hasAppliedInitialPosition = false

    init(initialLeadingFraction: CGFloat) {
        self.initialLeadingFraction = min(max(initialLeadingFraction, 0), 1)
        super.init(nibName: nil, bundle: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func viewDidLayout() {
        super.viewDidLayout()
        guard !hasAppliedInitialPosition,
              splitView.arrangedSubviews.count == 2,
              splitView.bounds.width > 0
        else { return }

        hasAppliedInitialPosition = true
        splitView.setPosition(
            Self.initialDividerPosition(
                totalWidth: splitView.bounds.width,
                leadingFraction: initialLeadingFraction
            ),
            ofDividerAt: 0
        )
    }

    static func initialDividerPosition(
        totalWidth: CGFloat,
        leadingFraction: CGFloat
    ) -> CGFloat {
        totalWidth * min(max(leadingFraction, 0), 1)
    }
}
